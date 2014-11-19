using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SimpleContainer.Helpers;
using SimpleContainer.Helpers.ReflectionEmit;
using SimpleContainer.Infection;

namespace SimpleContainer.Implementation
{
	public class DependenciesInjector
	{
		private readonly IResolveDependency resolver;
		private readonly ConcurrentDictionary<Type, Injection[]> injections = new ConcurrentDictionary<Type, Injection[]>();

		private const BindingFlags bindingFlags =
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		public DependenciesInjector(IResolveDependency resolver)
		{
			this.resolver = resolver;
		}

		public void BuildUp(object target)
		{
			var dependencies = GetInjections(target.GetType());
			foreach (var dependency in dependencies)
				dependency.accessor.Set(target, dependency.value);
		}

		public void BuildUpWithoutCache(object target)
		{
			var dependencies = GetInjections(target.GetType());
			foreach (var dependency in dependencies)
				dependency.accessor.Set(target, resolver.Get(dependency.accessor.MemberType));
		}

		public IEnumerable<Type> GetDependencies(Type type)
		{
			return GetInjections(type).Select(x => x.accessor.MemberType);
		}

		private Injection[] GetInjections(Type type)
		{
			return injections.GetOrAdd(type, DetectInjections);
		}

		private Injection[] DetectInjections(Type type)
		{
			var selfInjections = type
				.GetProperties(bindingFlags)
				.Where(m => m.CanWrite)
				.Union(type.GetFields(bindingFlags).Cast<MemberInfo>())
				.Where(m => m.IsDefined(typeof (InjectAttribute), true))
				.ToArray();
			Injection[] baseInjections = null;
			if (!type.IsDefined<FrameworkBoundaryAttribute>(false))
			{
				var baseType = type.BaseType;
				if (baseType != typeof (object))
					baseInjections = GetInjections(baseType);
			}
			var baseInjectionsCount = (baseInjections == null ? 0 : baseInjections.Length);
			var result = new Injection[selfInjections.Length + baseInjectionsCount];
			if (baseInjectionsCount > 0)
				Array.Copy(baseInjections, 0, result, 0, baseInjectionsCount);
			for (var i = 0; i < selfInjections.Length; i++)
			{
				var member = selfInjections[i];
				var resultIndex = i + baseInjectionsCount;
				result[resultIndex].value = resolver.Get(member.MemberType());
				result[resultIndex].accessor = MemberAccessor<object>.Get(member);
			}
			return result;
		}

		private struct Injection
		{
			public MemberAccessor<object> accessor;
			public object value;
		}
	}
}