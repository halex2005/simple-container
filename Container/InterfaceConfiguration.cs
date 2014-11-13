using System;
using System.Collections.Generic;

namespace SimpleContainer
{
	public class InterfaceConfiguration
	{
		public List<Type> ImplementationTypes { get; private set; }
		public object Implementation { get; set; }
		public Func<FactoryContext, object> Factory { get; set; }
		public bool UseAutosearch { get; set; }

		public void AddImplementation(Type type)
		{
			if (ImplementationTypes == null)
				ImplementationTypes = new List<Type>();
			if (!ImplementationTypes.Contains(type))
				ImplementationTypes.Add(type);
		}
	}
}