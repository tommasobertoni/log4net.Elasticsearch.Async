﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using log4net.Appender;
using log4net.Core;

namespace log4net.AsyncAppender
{
    public abstract class AsyncAppender : AppenderSkeleton
    {
        private readonly CancellationTokenSource _cts = new();
        private EventsHandler? _handler;

        public AsyncAppender()
        {
            Activated = false;
            AcceptsLoggingEvents = false;
        }

        #region Configuration properties

        public int MaxConcurrentProcessorsCount { get; set; } = 3;

        public int MaxBatchSize { get; set; } = 512;

        public int CloseTimeoutMillis { get; set; } = 5000;

        public IAsyncAppenderConfigurator? Configurator { get; set; }

        public bool Trace { get; set; }

        #endregion

        public bool Activated { get; protected set; }

        public bool AcceptsLoggingEvents { get; protected set; }

        public bool IsProcessing => _handler?.IsProcessing ?? false;

        protected abstract Task ProcessAsync(IReadOnlyList<LoggingEvent> events, CancellationToken cancellationToken);

        #region Setup

        public override void ActivateOptions()
        {
            base.ActivateOptions();

            Configure();

            if (ValidateSelf())
            {
                Activate();
            }
        }

        protected virtual void Configure()
        {
            try
            {
                Configurator?.Configure(this);
            }
            catch (Exception ex)
            {
                var message = "Error during configuration";
                TryTrace(message, ex);
                ErrorHandler?.Error(message, ex);
            }
        }

        protected virtual bool ValidateSelf()
        {
            try
            {
                if (MaxConcurrentProcessorsCount < 1)
                {
                    var message = $"{nameof(MaxConcurrentProcessorsCount)} must be positive.";
                    TryTrace(message);
                    ErrorHandler?.Error(message);
                    return false;
                }

                if (MaxBatchSize < 1)
                {
                    var message = $"{nameof(MaxBatchSize)} must be positive.";
                    TryTrace(message);
                    ErrorHandler?.Error(message);
                    return false;
                }

                if (CloseTimeoutMillis <= 0)
                {
                    var message = $"{nameof(CloseTimeoutMillis)} must be positive.";
                    TryTrace(message);
                    ErrorHandler?.Error(message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                var message = "Error during validation";
                TryTrace(message, ex);
                ErrorHandler?.Error(message, ex);
                return false;
            }

            return true;
        }

        protected virtual void Activate()
        {
            _handler = new EventsHandler(
                ProcessAsync,
                MaxConcurrentProcessorsCount,
                MaxBatchSize,
                _cts.Token)
            {
                ErrorHandler = (ex, events) =>
                {
                    var message = "An error occurred during events processing";
                    TryTrace(message, ex);
                    ErrorHandler?.Error(message, ex);
                },

                Tracer = message => TryTrace(message),
            };

            _handler.Start();

            Activated = true;
            AcceptsLoggingEvents = true;

            TryTrace("Activated");
        }

        #endregion

        #region Appending

        protected override void Append(LoggingEvent[] loggingEvents)
        {
            if (!Activated || !AcceptsLoggingEvents)
            {
                var message = "This appender cannot process logging events.";
                TryTrace(message);
                ErrorHandler?.Error(message);
                return;
            }

            _handler!.Handle(loggingEvents);
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (!Activated || !AcceptsLoggingEvents)
            {
                var message = "This appender cannot process logging events.";
                TryTrace(message);
                ErrorHandler?.Error(message);
                return;
            }

            _handler!.Handle(loggingEvent);
        }

        #endregion

        #region Termination

        ~AsyncAppender() => OnClose();

        protected override void OnClose()
        {
            base.OnClose();

            if (!Activated) return;

            AcceptsLoggingEvents = false;
            TryTrace("Closing");

            _cts.Cancel();
            _handler!.Dispose();

            var closingTimeout = Task.Delay(CloseTimeoutMillis);

            var processors = _handler.Processors;
            var processorsTermination = Task.WhenAll(processors);

            if (processors.Count > 0)
                TryTrace($"Waiting for {processors.Count} processors to terminate.");

            int completedTaskIndex = Task.WaitAny(closingTimeout, processorsTermination);
            if (completedTaskIndex == 0)
            {
                var message = $"Processors {processors.Count} termination timed out during appender OnClose.";
                TryTrace(message);
                ErrorHandler?.Error(message);
            }

            Activated = false;
            TryTrace("Deactivated");
        }

        #endregion

        #region State changed

        public Task ProcessingStarted() =>
            _handler?.ProcessingStarted() ?? throw new Exception("Appender was not activated");

        public Task ProcessingTerminated() =>
            _handler?.ProcessingTerminated() ?? throw new Exception("Appender was not activated");

        #endregion

        #region Trace

        protected void TryTrace(string message, Exception? exception = null)
        {
            if (!Trace) return;

            if (exception == null)
                System.Diagnostics.Trace.WriteLine(message);
            else
                System.Diagnostics.Trace.WriteLine($"{message}, exception: {exception.Message}");
        }

        #endregion
    }
}
