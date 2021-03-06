﻿using Shuttle.Core.Infrastructure;

namespace Shuttle.Esb
{
	public class ReceiveExceptionObserver :
		IPipelineObserver<OnPipelineException>
	{
		private readonly ILog _log;

		public ReceiveExceptionObserver()
		{
			_log = Log.For(this);
		}

		public void Execute(OnPipelineException pipelineEvent)
		{
			var state = pipelineEvent.Pipeline.State;
			var bus = state.GetServiceBus();

			bus.Events.OnBeforePipelineExceptionHandled(this, new PipelineExceptionEventArgs(pipelineEvent.Pipeline));

			try
			{
				if (pipelineEvent.Pipeline.ExceptionHandled)
				{
					return;
				}

				try
				{
					var transportMessage = state.GetTransportMessage();
					var receivedMessage = state.GetReceivedMessage();

					if (transportMessage == null)
					{
						if (receivedMessage != null)
						{
							state.GetWorkQueue().Release(receivedMessage.AcknowledgementToken);

							_log.Error(string.Format(EsbResources.ReceivePipelineExceptionMessageReleased,
								pipelineEvent.Pipeline.Exception.AllMessages()));
						}
						else
						{
							_log.Error(string.Format(EsbResources.ReceivePipelineExceptionMessageNotReceived,
								pipelineEvent.Pipeline.Exception.AllMessages()));
						}

						return;
					}

					var action = bus.Configuration.Policy.EvaluateMessageHandlingFailure(pipelineEvent);

					transportMessage.RegisterFailure(pipelineEvent.Pipeline.Exception.AllMessages(),
						action.TimeSpanToIgnoreRetriedMessage);

					using (var stream = bus.Configuration.Serializer.Serialize(transportMessage))
					{
						var handler = state.GetMessageHandler();
						var handlerFullTypeName = handler != null ? handler.GetType().FullName : "(handler is null)";
						var currentRetryCount = transportMessage.FailureMessages.Count;

						var retry = !(pipelineEvent.Pipeline.Exception is UnrecoverableHandlerException)
						            &&
						            action.Retry;

						if (retry)
						{
							_log.Warning(string.Format(EsbResources.MessageHandlerExceptionWillRetry,
								handlerFullTypeName,
								pipelineEvent.Pipeline.Exception.AllMessages(),
								transportMessage.MessageType,
								transportMessage.MessageId,
								currentRetryCount,
								state.GetMaximumFailureCount()));

							state.GetWorkQueue().Enqueue(transportMessage, stream);
						}
						else
						{
							_log.Error(string.Format(EsbResources.MessageHandlerExceptionFailure,
								handlerFullTypeName,
								pipelineEvent.Pipeline.Exception.AllMessages(),
								transportMessage.MessageType,
								transportMessage.MessageId,
								state.GetMaximumFailureCount(),
								state.GetErrorQueue().Uri));

							state.GetErrorQueue().Enqueue(transportMessage, stream);
						}
					}

					state.GetWorkQueue().Acknowledge(receivedMessage.AcknowledgementToken);
				}
				finally
				{
					pipelineEvent.Pipeline.MarkExceptionHandled();
					bus.Events.OnAfterPipelineExceptionHandled(this,
						new PipelineExceptionEventArgs(pipelineEvent.Pipeline));
				}
			}
			finally
			{
				pipelineEvent.Pipeline.Abort();
			}
		}
	}
}