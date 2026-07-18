using System.ComponentModel;
using System.Diagnostics;

namespace CodexQuotaWidget.Services;

public enum CodexProcessState
{
    NotRunning,
    Running,
    Unknown
}

public sealed class CodexProcessDetector
{
    public CodexProcessState GetState()
    {
        try
        {
            using var codexProcesses = new ProcessCollection(
                Process.GetProcessesByName("codex"));
            if (codexProcesses.Any())
            {
                return CodexProcessState.Running;
            }

            var encounteredUnknownPath = false;
            using var chatGptProcesses = new ProcessCollection(
                Process.GetProcessesByName("ChatGPT"));
            foreach (var process in chatGptProcesses)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path?.Contains("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return CodexProcessState.Running;
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException)
                {
                    encounteredUnknownPath = true;
                }
            }

            return encounteredUnknownPath
                ? CodexProcessState.Unknown
                : CodexProcessState.NotRunning;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            return CodexProcessState.Unknown;
        }
    }

    private sealed class ProcessCollection : IDisposable, IEnumerable<Process>
    {
        private readonly Process[] _processes;

        public ProcessCollection(Process[] processes)
        {
            _processes = processes;
        }

        public bool Any() => _processes.Length > 0;

        public IEnumerator<Process> GetEnumerator() =>
            ((IEnumerable<Process>)_processes).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public void Dispose()
        {
            foreach (var process in _processes)
            {
                process.Dispose();
            }
        }
    }
}
