using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SimpleContainer.Helpers;
using SimpleContainer.Implementation;

namespace SimpleContainer.Generics
{
	internal class GenericsAutoCloser
	{
		private readonly IInheritanceHierarchy hierarchy;
		private readonly Func<AssemblyName, bool> assemblyFilter;
		private readonly ConcurrentDictionary<Type, Type[]> cache = new ConcurrentDictionary<Type, Type[]>();

		public GenericsAutoCloser(IInheritanceHierarchy hierarchy, Func<AssemblyName, bool> assemblyFilter)
		{
			this.hierarchy = hierarchy;
			this.assemblyFilter = assemblyFilter;
		}

		public Type[] AutoCloseDefinition(Type type)
		{
			Type[] result;
			return cache.TryGetValue(type, out result) ? result : InferGenerics(type);
		}

		private Type[] InferGenerics(Type type)
		{
			var context = new Dictionary<Type, GenericDefinition>();
			Mark(type, context);
			Deduce(context);
			Publish(context);
			return context[type].closures.ToArray();
		}

		private void Publish(Dictionary<Type, GenericDefinition> context)
		{
			foreach (var p in context)
				cache.TryAdd(p.Key, p.Value.closures.ToArray());
		}

		private static void Deduce(Dictionary<Type, GenericDefinition> context)
		{
			var targets = new Queue<GenericDefinition>();
			foreach (var p in context)
				if (p.Value.closures.Count > 0)
					targets.Enqueue(p.Value);
			while (targets.Count > 0)
			{
				var target = targets.Dequeue();
				foreach (var referer in target.referers)
				{
					var refererClosed = false;
					foreach (var closure in target.closures)
					{
						var closedReferer = referer.isInterface
							? closure.ImplementationOf(referer.definition.type)
							: referer.definition.type.CloseByPattern(referer.pattern, closure);
						if (closedReferer != null)
							refererClosed |= referer.definition.closures.Add(closedReferer);
					}
					if (refererClosed)
						targets.Enqueue(referer.definition);
				}
			}
		}

		private GenericDefinition Mark(Type definition, Dictionary<Type, GenericDefinition> context)
		{
			GenericDefinition result;
			if (context.TryGetValue(definition, out result))
				return result;
			result = new GenericDefinition {type = definition};
			context.Add(definition, result);
			Type[] types;
			if (cache.TryGetValue(definition, out types))
				foreach (var type in types)
					result.closures.Add(type);
			else if (definition.IsAbstract)
				MarkInterface(result, context);
			else
				MarkImplementation(result, context);
			return result;
		}

		private void MarkImplementation(GenericDefinition definition, Dictionary<Type, GenericDefinition> context)
		{
			var ctor = definition.type.GetConstructor();
			if (!ctor.isOk)
				return;
			var parameters = ctor.value.GetParameters();
			var hasAnyGenericDependencies = false;
			foreach (var parameter in parameters)
			{
				var parameterType = parameter.ParameterType;
				if (parameterType.IsGenericType && (parameterType.GetGenericTypeDefinition() == typeof (IEnumerable<>)
				                                    || parameterType.GetGenericTypeDefinition() == typeof (Func<>)))
					parameterType = parameterType.GetGenericArguments()[0];
				if (parameterType.IsSimpleType())
					continue;
				if (!assemblyFilter(parameterType.Assembly.GetName()))
					continue;
				if (!parameterType.IsGenericType)
					continue;
				if (!parameterType.ContainsGenericParameters)
					continue;
				if (!TypeHelpers.HasEquivalentParameters(parameterType, definition.type))
					continue;
				hasAnyGenericDependencies = true;
				Mark(parameterType.GetGenericTypeDefinition(), context).referers.Add(new GenericReferer
				{
					definition = definition,
					pattern = parameterType
				});
			}
			if (!hasAnyGenericDependencies)
				MarkImplementationConstraints(definition);
		}

		private void MarkInterface(GenericDefinition definition, Dictionary<Type, GenericDefinition> context)
		{
			var implementationTypes = hierarchy.GetOrNull(definition.type);
			if (implementationTypes == null)
				return;
			foreach (var implType in implementationTypes)
			{
				var implementedInterface = implType.ImplementationOf(definition.type);
				if (implType.IsGenericType)
					Mark(implType, context).referers.Add(new GenericReferer
					{
						definition = definition,
						pattern = implementedInterface,
						isInterface = true
					});
				else
				{
					var closure = definition.type.CloseByPattern(definition.type, implementedInterface);
					if (closure != null)
						definition.closures.Add(closure);
				}
			}
		}

		private void MarkImplementationConstraints(GenericDefinition definition)
		{
			var genericArguments = definition.type.GetGenericArguments();
			if (genericArguments.Length != 1)
				return;
			var constraints = genericArguments[0].GetGenericParameterConstraints();
			if (constraints.Length == 0)
				return;
			foreach (var c in constraints)
				if (!assemblyFilter(c.Assembly.GetName()))
					return;
			var impls = new List<Type>(hierarchy.GetOrNull(constraints[0]) ?? Type.EmptyTypes);
			for (var i = 1; i < constraints.Length; i++)
			{
				if (impls.Count == 0)
					return;
				var current = (hierarchy.GetOrNull(constraints[i]) ?? Type.EmptyTypes).ToArray();
				for (var j = impls.Count - 1; j >= 0; j--)
					if (Array.IndexOf(current, impls[j]) < 0)
						impls.RemoveAt(j);
			}
			if (impls.Count == 0)
				return;
			var nonGenericOverrides = (hierarchy.GetOrNull(definition.type) ?? Type.EmptyTypes)
				.Where(x => !x.IsGenericType)
				.ToArray();
			foreach (var impl in impls)
			{
				if (genericArguments[0].GenericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint))
					if (impl.GetConstructor(Type.EmptyTypes) == null)
						continue;
				var closedItem = definition.type.MakeGenericType(impl);
				var overriden = false;
				foreach (var nonGenericOverride in nonGenericOverrides)
					if (closedItem.IsAssignableFrom(nonGenericOverride))
					{
						overriden = true;
						break;
					}
				if (!overriden)
					definition.closures.Add(closedItem);
			}
		}

		private class GenericDefinition
		{
			public readonly List<GenericReferer> referers = new List<GenericReferer>();
			public readonly HashSet<Type> closures = new HashSet<Type>();
			public Type type;
		}

		private class GenericReferer
		{
			public GenericDefinition definition;
			public Type pattern;
			public bool isInterface;
		}
	}
}