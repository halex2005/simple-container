using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SimpleContainer.Helpers.ReflectionEmit;

namespace SimpleContainer.Helpers
{
	public static class ObjectAccessor
	{
		private static readonly ConcurrentDictionary<Type, TypeAccessor> typeAccessors =
			new ConcurrentDictionary<Type, TypeAccessor>();

		private static readonly Func<Type, TypeAccessor> createTypeAccessor = t =>
		{
			IDictionary<string, IMemberAccessor> properties = t.GetProperties()
				.ToDictionary(x => x.Name, UntypedMemberAccessor.Create);
			return new TypeAccessor(properties);
		};

		public static IObjectAccessor Get(object o)
		{
			return o == null ? null : new ObjectAccessorImpl(typeAccessors.GetOrAdd(o.GetType(), createTypeAccessor), o);
		}

		private class ObjectAccessorImpl : IObjectAccessor
		{
			private readonly TypeAccessor typeAccessor;
			private readonly object obj;

			public ObjectAccessorImpl(TypeAccessor typeAccessor, object obj)
			{
				this.typeAccessor = typeAccessor;
				this.obj = obj;
			}

			public bool TryGet(string name, out object value)
			{
				return typeAccessor.TryGet(obj, name, out value);
			}
		}

		private class TypeAccessor
		{
			private readonly IDictionary<string, IMemberAccessor> properties;

			public TypeAccessor(IDictionary<string, IMemberAccessor> properties)
			{
				this.properties = properties;
			}

			public bool TryGet(object o, string name, out object value)
			{
				IMemberAccessor accessor;
				if (properties.TryGetValue(name, out accessor))
				{
					value = accessor.Get(o);
					return true;
				}
				value = null;
				return false;
			}
		}
	}
}