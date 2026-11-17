using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitBatchNWCExport.Export
{
    /// <summary>
    /// Пока идёт пакетный экспорт, окна не должны ждать пользователя. Три уровня:
    /// 1) FailuresProcessing — предупреждения удаляются, ошибки разрешаются штатным способом Revit;
    /// 2) DialogBoxShowing — окна Revit (TaskDialog, MessageBox, диалоги) закрываются подходящей кнопкой;
    /// 3) сторожевой таймер — нативные окна Windows (#32770) процесса Revit, которые не проходят
    ///    через API (окна надстроек, экспортёра Navisworks, системные сообщения), закрываются кнопкой или WM_CLOSE.
    /// Всё закрытое собирается в журнал и попадает в отчёт.
    /// </summary>
    public sealed class DialogSuppressor : IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly ConcurrentQueue<string> _journal = new ConcurrentQueue<string>();
        private readonly NativeDialogWatchdog _watchdog;

        // Известные окна Revit и кнопка, при которой работа продолжается
        private static readonly (string IdPart, TaskDialogResult Result)[] KnownDialogs =
        {
            ("Unresolved_References", TaskDialogResult.CommandLink2),        // Игнорировать и продолжить открытие
            ("Missing_Third_Party_Updater", TaskDialogResult.CommandLink1),  // Продолжить работу с файлом
            ("Third_Party_Updater", TaskDialogResult.CommandLink1),
            ("Cannot_Find_Central_Model", TaskDialogResult.Close),
            ("Location_Position_Changed", TaskDialogResult.CommandLink2),    // Не сохранять
            ("Save_Changes", TaskDialogResult.No),
            ("Changes_Not_Saved", TaskDialogResult.No),
            ("Audit", TaskDialogResult.Close),
        };

        public DialogSuppressor(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _uiApp.Application.FailuresProcessing += OnFailuresProcessing;
            _uiApp.DialogBoxShowing += OnDialogBoxShowing;
            _watchdog = new NativeDialogWatchdog(entry => _journal.Enqueue(entry));
        }

        /// <summary>Закрытые окна и обработанные ошибки с прошлого вызова.</summary>
        public List<string> TakeJournal()
        {
            var list = new List<string>();
            while (_journal.TryDequeue(out var entry)) list.Add(entry);
            return list;
        }

        public void Dispose()
        {
            _watchdog.Dispose();
            _uiApp.Application.FailuresProcessing -= OnFailuresProcessing;
            _uiApp.DialogBoxShowing -= OnDialogBoxShowing;
        }

        private void OnFailuresProcessing(object sender, FailuresProcessingEventArgs e)
        {
            var accessor = e.GetFailuresAccessor();
            var failures = accessor.GetFailureMessages().ToList();
            if (failures.Count == 0) return;

            bool resolved = false;
            foreach (var failure in failures)
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error && failure.HasResolutions())
                {
                    accessor.ResolveFailure(failure);
                    resolved = true;
                    _journal.Enqueue("ошибка Revit разрешена автоматически: " + failure.GetDescriptionText());
                }
                else
                {
                    _journal.Enqueue("ошибка Revit: " + failure.GetDescriptionText());
                }
            }
            e.SetProcessingResult(resolved ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue);
        }

        private void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            string id = e.DialogId ?? "";
            string text = (e as TaskDialogShowingEventArgs)?.Message ?? (e as MessageBoxShowingEventArgs)?.Message ?? "";
            try
            {
                bool done = false;
                foreach (var known in KnownDialogs)
                {
                    if (id.IndexOf(known.IdPart, StringComparison.OrdinalIgnoreCase) >= 0 && e.OverrideResult((int)known.Result))
                    {
                        done = true;
                        break;
                    }
                }

                if (!done)
                {
                    // MessageBox: IDOK, IDYES нельзя — только «ОК», «Нет», «Отмена»
                    int[] candidates = e is MessageBoxShowingEventArgs
                        ? new[] { 1, 7, 2 }
                        : new[]
                        {
                            (int)TaskDialogResult.Ok, (int)TaskDialogResult.Close, (int)TaskDialogResult.Cancel,
                            (int)TaskDialogResult.CommandLink1, (int)TaskDialogResult.No
                        };
                    done = candidates.Any(e.OverrideResult);
                }

                _journal.Enqueue($"окно Revit {(done ? "закрыто" : "не удалось закрыть")}: {Describe(id, text)}");
            }
            catch (Exception ex)
            {
                _journal.Enqueue($"ошибка закрытия окна {Describe(id, text)}: {ex.Message}");
            }
        }

        private static string Describe(string id, string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 160) text = text.Substring(0, 160) + "…";
            return string.IsNullOrEmpty(text) ? id : $"{id} «{text}»";
        }
    }

    /// <summary>Закрывает нативные модальные окна процесса Revit, которые не видны через Revit API.</summary>
    internal sealed class NativeDialogWatchdog : IDisposable
    {
        private const string DialogClass = "#32770";
        private const int WM_CLOSE = 0x0010;
        private const int BM_CLICK = 0x00F5;
        private const int MaxClicksBeforeClose = 2;

        // Кнопки, которые продолжают работу, в порядке предпочтения. «Да» и «Сохранить» не нажимаем.
        private static readonly string[] PreferredButtons =
        {
            "ok", "ок", "закрыть", "close", "продолжить", "continue", "игнорировать", "ignore",
            "пропустить", "skip", "нет", "no", "не сохранять", "don't save", "отмена", "cancel"
        };

        private readonly Action<string> _journal;
        private readonly uint _processId = (uint)Process.GetCurrentProcess().Id;
        private readonly Dictionary<IntPtr, int> _attempts = new Dictionary<IntPtr, int>();
        private readonly Timer _timer;
        private int _busy;

        public NativeDialogWatchdog(Action<string> journal)
        {
            _journal = journal;
            _timer = new Timer(_ => Tick(), null, 1000, 700);
        }

        public void Dispose()
        {
            using (var done = new ManualResetEvent(false))
            {
                if (_timer.Dispose(done)) done.WaitOne(2000);
            }
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var dialogs = new List<IntPtr>();
                EnumWindows((hwnd, _) =>
                {
                    if (IsWindowVisible(hwnd) && GetClass(hwnd) == DialogClass)
                    {
                        GetWindowThreadProcessId(hwnd, out uint pid);
                        if (pid == _processId) dialogs.Add(hwnd);
                    }
                    return true;
                }, IntPtr.Zero);

                foreach (var hwnd in dialogs) Dismiss(hwnd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RevitBatchNWCExport: сторож окон: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void Dismiss(IntPtr hwnd)
        {
            _attempts.TryGetValue(hwnd, out int attempts);
            _attempts[hwnd] = attempts + 1;

            string title = GetText(hwnd);
            var buttons = new List<(IntPtr Handle, string Text)>();
            var texts = new List<string>();
            EnumChildWindows(hwnd, (child, _) =>
            {
                string cls = GetClass(child);
                string text = GetText(child);
                if (cls == "Button" && IsWindowVisible(child) && IsWindowEnabled(child)) buttons.Add((child, Clean(text)));
                else if (cls == "Static" && !string.IsNullOrWhiteSpace(text)) texts.Add(text.Trim());
                return true;
            }, IntPtr.Zero);

            string message = string.Join(" ", texts);
            if (message.Length > 160) message = message.Substring(0, 160) + "…";

            if (attempts < MaxClicksBeforeClose)
            {
                foreach (var preferred in PreferredButtons)
                {
                    var button = buttons.FirstOrDefault(b => string.Equals(b.Text, preferred, StringComparison.OrdinalIgnoreCase));
                    if (button.Handle == IntPtr.Zero) continue;
                    PostMessage(button.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                    if (attempts == 0) _journal($"окно Windows «{title}» закрыто кнопкой «{button.Text}»: {message}");
                    return;
                }
            }

            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (attempts == 0 || attempts == MaxClicksBeforeClose)
                _journal($"окно Windows «{title}» закрыто: {message}");
        }

        private static string Clean(string text) => (text ?? "").Replace("&", "").Trim();

        private static string GetClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetText(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0) return "";
            var sb = new StringBuilder(length + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
