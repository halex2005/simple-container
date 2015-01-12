﻿using System;
using System.Collections.Generic;
using SimpleContainer.Configuration;
using SimpleContainer.Interface;

namespace SimpleContainer
{
	public interface IContainer : IDisposable
	{
		IEnumerable<Type> GetDependencies(Type type);
		IEnumerable<Type> GetImplementationsOf(Type interfaceType);
		IEnumerable<object> GetAll(Type type);
		object Get(Type type, IEnumerable<string> contracts, bool dumpConstructionLog = false);
		object Create(Type type, IEnumerable<string> contracts, object arguments);
		void BuildUp(object target, IEnumerable<string> contracts);

		void DumpConstructionLog(Type type, IEnumerable<string> contracts, bool entireResolutionContext,
			ISimpleLogWriter writer);

		IContainer Clone(Action<ContainerConfigurationBuilder> configure);
	}
}