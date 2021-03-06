using System;
using SimpleContainer.Interface;

namespace SimpleContainer.Implementation
{
	internal class InstanceWrap
	{
		private volatile bool initialized;
		public object Instance { get; private set; }
		public bool Owned { get; private set; }
		public bool IsConstant { get; private set; }

		public InstanceWrap(object instance, bool owned, bool isConstant)
		{
			Instance = instance;
			Owned = owned;
			IsConstant = isConstant;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != GetType()) return false;
			return ReferenceEquals(Instance, ((InstanceWrap) obj).Instance);
		}

		public void EnsureInitialized(ContainerService service, ContainerContext containerContext, ContainerService root)
		{
			if (!Owned)
				return;
			var componentInstance = Instance as IInitializable;
			if (componentInstance == null)
				return;
			if (!initialized)
				lock (this)
					if (!initialized)
					{
						var name = new ServiceName(Instance.GetType(), service.UsedContracts);
						containerContext.infoLogger?.Invoke(name, "initialize started");
						try
						{
							componentInstance.Initialize();
						}
						catch (Exception e)
						{
							var message = string.Format("exception initializing {0}{1}{1}{2}",
								name, Environment.NewLine, root.GetConstructionLog(containerContext));
							throw new SimpleContainerException(message, e);
						}

						containerContext.infoLogger?.Invoke(name, "initialize finished");
						initialized = true;
					}
		}

		public override int GetHashCode()
		{
			return Instance.GetHashCode();
		}
	}
}