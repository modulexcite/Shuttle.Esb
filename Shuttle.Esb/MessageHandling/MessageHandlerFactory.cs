using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shuttle.Core.Infrastructure;

namespace Shuttle.Esb
{
	public abstract class MessageHandlerFactory : IMessageHandlerFactory
	{
		private static readonly object Padlock = new object();

		private readonly List<IMessageHandler> _releasedHandlers = new List<IMessageHandler>();

		private readonly Type _messageHandlerType = typeof (IMessageHandler<>);

		public IMessageHandler GetHandler(object message)
		{
			Guard.AgainstNull(message, "message");

			var messageType = message.GetType();

			lock (Padlock)
			{
				var handler = _releasedHandlers.Find(candidate =>
				{
					foreach (var arguments in
						candidate.GetType().InterfacesAssignableTo(_messageHandlerType)
							.Select(type => type.GetGenericArguments()))
					{
						if (arguments.Length != 1)
						{
							return false;
						}

						if (arguments[0] == messageType)
						{
							return true;
						}
					}

					return false;
				});

				if (handler != null)
				{
					_releasedHandlers.Remove(handler);

					return handler;
				}
			}

			return CreateHandler(message);
		}

		public abstract IMessageHandler CreateHandler(object message);

		public virtual void ReleaseHandler(IMessageHandler handler)
		{
			if (handler == null)
			{
				return;
			}

			if (!handler.IsReusable)
			{
				handler.AttemptDispose();

				return;
			}

			lock (Padlock)
			{
				if (!_releasedHandlers.Contains(handler))
				{
					_releasedHandlers.Add(handler);
				}
			}
		}

		public abstract IEnumerable<Type> MessageTypesHandled { get; }

        public IMessageHandlerFactory RegisterHandlers()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                RegisterHandlers(assembly);
            }

            return this;
        }

	    public abstract IMessageHandlerFactory RegisterHandlers(Assembly assembly);
	}
}