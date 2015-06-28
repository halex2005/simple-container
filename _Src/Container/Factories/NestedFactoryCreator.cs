using System;
using SimpleContainer.Implementation;

namespace SimpleContainer.Factories
{
	internal static class NestedFactoryCreator
	{
		public static bool TryCreate(ContainerService.Builder builder)
		{
			var factoryType = builder.Type.GetNestedType("Factory");
			if (factoryType == null)
				return false;
			var method = factoryType.GetMethod("Create", Type.EmptyTypes);
			if (method == null)
				return false;
			var factory = builder.Context.Container.ResolveSingleton(method.DeclaringType, builder.Context);
			var dependency = factory.AsSingleInstanceDependency(null);
			builder.AddDependency(dependency, false);
			if (dependency.Status == ServiceStatus.Ok)
				builder.CreateInstance(method, dependency.Value, new object[0]);
			return true;
		}
	}
}