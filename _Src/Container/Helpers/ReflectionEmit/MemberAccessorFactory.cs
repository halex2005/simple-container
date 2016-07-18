﻿using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SimpleContainer.Helpers.ReflectionEmit
{
	internal abstract class MemberAccessorFactory<TOutput>
	{
		protected static void EmitBoxingCast(Type memberType, ILGenerator ilGenerator)
		{
			var caster = new BoxingCaster(typeof (TOutput), memberType);
			caster.EmitCast(ilGenerator);
		}

		protected static void EmitUnboxingCast(Type memberType, ILGenerator ilGenerator)
		{
			var caster = new UnboxingCaster(typeof (TOutput), memberType);
			caster.EmitCast(ilGenerator);
		}

		public static MemberAccessorFactory<TOutput> Create(MemberInfo memberInfo)
		{
			if (memberInfo is FieldInfo)
				return new FieldAccessorFactory<TOutput>(memberInfo as FieldInfo);
			if (memberInfo is PropertyInfo)
				return new PropertyAccessorFactory<TOutput>(memberInfo as PropertyInfo);
			throw new InvalidMemberInfoException();
		}

		protected static void EmitLoadTarget(ILGenerator ilGenerator, MemberInfo member)
		{
			if (member.IsStatic())
				return;
			ilGenerator.Emit(OpCodes.Ldarg_0);
			var declaringType = member.DeclaringType;
			if (!declaringType.IsValueType())
				return;
			ilGenerator.Emit(OpCodes.Unbox_Any, declaringType);
			ilGenerator.DeclareLocal(declaringType);
			ilGenerator.Emit(OpCodes.Stloc_0);
			ilGenerator.Emit(OpCodes.Ldloca_S, 0);
		}

		public Action<object, TOutput> CreateSetter()
		{
			var method = CreateSettingMethod();
			return TryEmitSet(method.GetILGenerator()) ? CreateSettingDelegate(method) : null;
		}

		public Func<object, TOutput> CreateGetter()
		{
			var method = CreateGettingMethod();
			return TryEmitGet(method.GetILGenerator()) ? CreateGettingDelegate(method) : null;
		}

		protected abstract bool TryEmitSet(ILGenerator ilGenerator);
		protected abstract bool TryEmitGet(ILGenerator ilGenerator);

		private static Action<object, TOutput> CreateSettingDelegate(DynamicMethod dynamicMethod)
		{
			return (Action<object, TOutput>) dynamicMethod.CreateDelegate(typeof (Action<object, TOutput>));
		}

		private static Func<object, TOutput> CreateGettingDelegate(DynamicMethod dynamicMethod)
		{
			return (Func<object, TOutput>) dynamicMethod.CreateDelegate(typeof (Func<object, TOutput>));
		}

		private static DynamicMethod CreateSettingMethod()
		{
			return new DynamicMethod("",
				null,
				new[] {typeof (object), typeof (TOutput)},
				typeof (MemberAccessorFactory<TOutput>),
				true);
		}

		private static DynamicMethod CreateGettingMethod()
		{
			return new DynamicMethod("",
				typeof (TOutput),
				new[] {typeof (object)},
				typeof (MemberAccessorFactory<TOutput>),
				true);
		}
	}
}