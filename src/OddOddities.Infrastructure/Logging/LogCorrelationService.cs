using OddOddities.Application.Ports;
using Serilog.Context;

namespace OddOddities.Infrastructure.Logging;

/// <summary>
/// Implements log correlation by pushing execution context properties onto Serilog's LogContext.
/// Each pipeline step pushes its correlation properties so they appear in all subsequent logs
/// until the IDisposable is disposed.
/// </summary>
public sealed class LogCorrelationService : ILogCorrelationPort
{
    /// <inheritdoc />
    public IDisposable PushCorrelation(
        string executionId,
        string step,
        string outcome,
        long durationMs)
    {
        var disposable1 = LogContext.PushProperty("executionId", executionId);
        var disposable2 = LogContext.PushProperty("step", step);
        var disposable3 = LogContext.PushProperty("outcome", outcome);
        var disposable4 = LogContext.PushProperty("durationMs", durationMs);

        return new CompositeDisposable(disposable1, disposable2, disposable3, disposable4);
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public CompositeDisposable(params IDisposable[] disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
