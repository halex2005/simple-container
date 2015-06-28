using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleContainer.Configuration;
using SimpleContainer.Factories;
using SimpleContainer.Generics;
using SimpleContainer.Helpers;
using SimpleContainer.Infection;
using SimpleContainer.Interface;

namespace SimpleContainer.Implementation
{
	internal class SimpleContainer : IContainer
	{
		private readonly Func<ServiceName, ContainerServiceId> createWrap;

		private readonly ConcurrentDictionary<ServiceName, ContainerServiceId> instanceCache =
			new ConcurrentDictionary<ServiceName, ContainerServiceId>();

		private readonly DependenciesInjector dependenciesInjector;
		private bool disposed;

		private readonly GenericsAutoCloser genericsAutoCloser;
		protected readonly IInheritanceHierarchy inheritors;
		protected readonly LogError errorLogger;
		protected readonly LogInfo infoLogger;
		private readonly ImplementationSelector[] implementationSelectors;
		internal IConfigurationRegistry Configuration { get; private set; }

		public SimpleContainer(GenericsAutoCloser genericsAutoCloser, IConfigurationRegistry configurationRegistry,
			IInheritanceHierarchy inheritors,LogError errorLogger, LogInfo infoLogger)
		{
			Configuration = configurationRegistry;
			implementationSelectors = configurationRegistry.GetImplementationSelectors();
			this.genericsAutoCloser = genericsAutoCloser;
			this.inheritors = inheritors;
			dependenciesInjector = new DependenciesInjector(this);
			createWrap = k => new ContainerServiceId();
			this.errorLogger = errorLogger;
			this.infoLogger = infoLogger;
		}

		public ResolvedService Resolve(Type type, IEnumerable<string> contracts)
		{
			EnsureNotDisposed();
			if (type == null)
				throw new ArgumentNullException("type");
			var contractsArray = CheckContracts(contracts);
			var typeToResolve = type.UnwrapEnumerable();
			var cacheKey = new ServiceName(typeToResolve, InternalHelpers.ToInternalContracts(contractsArray, typeToResolve));
			var wrap = instanceCache.GetOrAdd(cacheKey, createWrap);
			ContainerService result;
			if (!wrap.TryGet(out result))
				result = ResolveSingleton(cacheKey.Type, new ResolutionContext(this, cacheKey.Contracts));
			return new ResolvedService(result, this, typeToResolve != type);
		}

		internal ContainerService Create(Type type, IEnumerable<string> contracts, object arguments, ResolutionContext context)
		{
			context = context ?? new ResolutionContext(this, InternalHelpers.ToInternalContracts(contracts, type));
			return context.Instantiate(type, true, ObjectAccessor.Get(arguments));
		}

		private static string[] CheckContracts(IEnumerable<string> contracts)
		{
			if (contracts == null)
				return null;
			var contractsArray = contracts.ToArray();
			foreach (var contract in contractsArray)
				if (string.IsNullOrEmpty(contract))
				{
					var message = string.Format("invalid contracts [{0}]", contractsArray.Select(x => x ?? "<null>").JoinStrings(","));
					throw new SimpleContainerException(message);
				}
			return contractsArray;
		}

		public ResolvedService Create(Type type, IEnumerable<string> contracts, object arguments)
		{
			EnsureNotDisposed();
			if (type == null)
				throw new ArgumentNullException("type");
			var contractsArray = CheckContracts(contracts);

			var result = Create(type, contractsArray, arguments, null);
			return new ResolvedService(result, this, false);
		}

		internal void Run(ContainerService containerService, string constructionLog)
		{
			if (constructionLog != null && infoLogger != null)
				infoLogger(new ServiceName(containerService.Type, containerService.UsedContracts), "\r\n" + constructionLog);
			containerService.EnsureRunCalled(infoLogger);
		}

		private ServiceConfiguration GetConfigurationWithoutContracts(Type type)
		{
			return GetConfigurationOrNull(type, new List<string>());
		}

		internal ServiceConfiguration GetConfiguration(Type type, ResolutionContext context)
		{
			return GetConfigurationOrNull(type, context.Contracts) ?? ServiceConfiguration.empty;
		}

		private ServiceConfiguration GetConfigurationOrNull(Type type, List<string> contracts)
		{
			var result = Configuration.GetConfigurationOrNull(type, contracts);
			if (result == null && type.IsGenericType)
				result = Configuration.GetConfigurationOrNull(type.GetDefinition(), contracts);
			return result;
		}

		public IEnumerable<Type> GetImplementationsOf(Type interfaceType)
		{
			EnsureNotDisposed();
			if (interfaceType == null)
				throw new ArgumentNullException("interfaceType");
			var interfaceConfiguration = GetConfigurationWithoutContracts(interfaceType);
			if (interfaceConfiguration != null && interfaceConfiguration.ImplementationTypes != null)
				return interfaceConfiguration.ImplementationTypes;
			var result = inheritors.GetOrNull(interfaceType);
			return result != null
				? result.Where(delegate(Type type)
				{
					var implementationConfiguration = GetConfigurationWithoutContracts(type);
					return implementationConfiguration == null || !implementationConfiguration.DontUseIt;
				}).ToArray()
				: Type.EmptyTypes;
		}

		public BuiltUpService BuildUp(object target, IEnumerable<string> contracts)
		{
			EnsureNotDisposed();
			if (target == null)
				throw new ArgumentNullException("target");
			var contractsArray = CheckContracts(contracts);

			return dependenciesInjector.BuildUp(target, contractsArray);
		}

		private IEnumerable<NamedInstance> GetInstanceCache(Type interfaceType)
		{
			var seen = new HashSet<object>();
			var target = new List<NamedInstance>();
			foreach (var wrap in instanceCache.Values)
			{
				ContainerService service;
				if (wrap.TryGet(out service))
					service.CollectInstances(interfaceType, seen, target);
			}
			return target;
		}

		public IContainer Clone(Action<ContainerConfigurationBuilder> configure)
		{
			EnsureNotDisposed();
			return new SimpleContainer(genericsAutoCloser, CloneConfiguration(configure), inheritors, null, infoLogger);
		}

		protected IConfigurationRegistry CloneConfiguration(Action<ContainerConfigurationBuilder> configure)
		{
			if (configure == null)
				return Configuration;
			var builder = new ContainerConfigurationBuilder();
			configure(builder);
			return new MergedConfiguration(Configuration, builder.RegistryBuilder.Build());
		}

		internal ContainerService ResolveSingleton(Type type, ResolutionContext context)
		{
			var cacheKey = new ServiceName(type, context.Contracts.ToArray());
			var id = instanceCache.GetOrAdd(cacheKey, createWrap);
			ContainerService result;
			if (!id.AcquireInstantiateLock(out result))
				return result;
			result = context.Instantiate(type, false, null);
			id.ReleaseInstantiateLock(result);
			return result;
		}

		internal void Instantiate(ContainerService.Builder builder)
		{
			LifestyleAttribute lifestyle;
			if (builder.Type.IsSimpleType())
				builder.SetError("can't create simple type");
			else if (builder.Type == typeof (IContainer))
				builder.AddInstance(this, false);
			else if (builder.Configuration.ImplementationAssigned)
				builder.AddInstance(builder.Configuration.Implementation, builder.Configuration.ContainerOwnsInstance);
			else if (builder.Configuration.Factory != null)
				builder.CreateInstanceBy(() => builder.Configuration.Factory(this), builder.Configuration.ContainerOwnsInstance);
			else if (builder.Configuration.FactoryWithTarget != null)
			{
				var previousService = builder.Context.GetPreviousService();
				var target = previousService == null ? null : previousService.Type;
				builder.CreateInstanceBy(() => builder.Configuration.FactoryWithTarget(this, target),
					builder.Configuration.ContainerOwnsInstance);
			}
			else if (builder.Type.IsValueType)
				builder.SetError("can't create value type");
			else if (builder.Type.IsGenericType && builder.Type.ContainsGenericParameters)
				builder.SetError("can't create open generic");
			else if (!builder.CreateNew && builder.Type.TryGetCustomAttribute(out lifestyle) &&
			         lifestyle.Lifestyle == Lifestyle.PerRequest)
			{
				const string messageFormat = "service [{0}] with PerRequest lifestyle can't be resolved, use Func<{0}> instead";
				builder.SetError(string.Format(messageFormat, builder.Type.FormatName()));
			}
			else if (builder.Type.IsAbstract)
				InstantiateInterface(builder);
			else
				InstantiateImplementation(builder);

			if (builder.Configuration.InstanceFilter != null)
			{
				var filteredOutCount = builder.FilterInstances(builder.Configuration.InstanceFilter);
				if (filteredOutCount > 0)
					builder.SetComment("instance filter");
			}
		}

		private void InstantiateInterface(ContainerService.Builder builder)
		{
			var implementationTypes = GetInterfaceImplementationTypes(builder).Distinct().ToList();
			List<ImplementationSelectorDecision> selectorDecisions = null;
			if (builder.HasNoConfiguration() && implementationSelectors.Length > 0)
			{
				selectorDecisions = new List<ImplementationSelectorDecision>();
				var typesArray = implementationTypes.ToArray();
				foreach (var s in implementationSelectors)
					s(builder.Type, typesArray, selectorDecisions);
				foreach (var decision in selectorDecisions)
					if (decision.action == ImplementationSelectorDecision.Action.Include)
						implementationTypes.Add(decision.target);
			}
			if (implementationTypes.Count == 0)
			{
				builder.SetComment("has no implementations");
				return;
			}
			foreach (var implementationType in implementationTypes)
			{
				ContainerService implementationService = null;
				string comment = null;

				var configuration = GetConfiguration(implementationType, builder.Context);
				if (configuration.IgnoredImplementation || implementationType.IsDefined("IgnoredImplementationAttribute"))
					comment = "IgnoredImplementation";
				else if (builder.CreateNew)
					implementationService = builder.Context.Instantiate(implementationType, true, builder.Arguments);
				else
				{
					ImplementationSelectorDecision? decision = null;
					if (selectorDecisions != null)
						foreach (var d in selectorDecisions)
							if (d.target == implementationType)
							{
								decision = d;
								break;
							}
					if (decision.HasValue)
						comment = decision.Value.comment;
					if (!decision.HasValue || decision.Value.action == ImplementationSelectorDecision.Action.Include)
						implementationService = builder.Context.Resolve(implementationType, null);
				}
				if (implementationService != null)
					builder.LinkTo(implementationService, comment);
				else
				{
					var dependency = ServiceDependency.NotResolved(null, implementationType.FormatName());
					dependency.Comment = comment;
					builder.AddDependency(dependency, true);
				}
				if (builder.Status.IsBad())
					return;
			}
		}

		private IEnumerable<Type> GetInterfaceImplementationTypes(ContainerService.Builder builder)
		{
			var candidates = new List<Type>();
			if (builder.Configuration.ImplementationTypes != null)
				candidates.AddRange(builder.Configuration.ImplementationTypes);
			if (builder.Configuration.ImplementationTypes == null || builder.Configuration.UseAutosearch)
				candidates.AddRange(inheritors.GetOrNull(builder.Type.GetDefinition()).EmptyIfNull());
			foreach (var implType in candidates)
			{
				if (!implType.IsGenericType)
				{
					if (!builder.Type.IsGenericType || builder.Type.IsAssignableFrom(implType))
						yield return implType;
				}
				else if (!implType.ContainsGenericParameters)
					yield return implType;
				else
				{
					var mapped = genericsAutoCloser.AutoCloseDefinition(implType);
					foreach (var type in mapped)
						if (builder.Type.IsAssignableFrom(type))
							yield return type;
					if (builder.Type.IsGenericType)
						foreach (var type in implType.CloseBy(builder.Type, implType))
							yield return type;
					if (builder.Arguments == null)
						continue;
					var serviceConstructor = implType.GetConstructor();
					if (!serviceConstructor.isOk)
						continue;
					foreach (var formalParameter in serviceConstructor.value.GetParameters())
					{
						if (!formalParameter.ParameterType.ContainsGenericParameters)
							continue;
						object parameterValue;
						if (!builder.Arguments.TryGet(formalParameter.Name, out parameterValue))
							continue;
						foreach (var type in implType.CloseBy(formalParameter.ParameterType, parameterValue.GetType()))
							yield return type;
					}
				}
			}
		}

		public IEnumerable<Type> GetDependencies(Type type)
		{
			EnsureNotDisposed();
			if (typeof (Delegate).IsAssignableFrom(type))
				return Enumerable.Empty<Type>();
			if (!type.IsAbstract)
			{
				var result = dependenciesInjector.GetDependencies(type)
					.Select(ReflectionHelpers.UnwrapEnumerable)
					.ToArray();
				if (result.Any())
					return result;
			}
			var serviceConstructor = type.GetConstructor();
			if (!serviceConstructor.isOk)
				return Enumerable.Empty<Type>();
			var typeConfiguration = GetConfigurationWithoutContracts(type);
			return serviceConstructor.value.GetParameters()
				.Where(p => typeConfiguration == null || typeConfiguration.GetOrNull(p) == null)
				.Select(x => x.ParameterType)
				.Select(ReflectionHelpers.UnwrapEnumerable)
				.Where(p => GetConfigurationWithoutContracts(p) == null)
				.Where(IsDependency);
		}

		private static bool IsDependency(Type type)
		{
			if (typeof (Delegate).IsAssignableFrom(type))
				return false;
			if (type.IsSimpleType())
				return false;
			if (type.IsArray && type.GetElementType().IsSimpleType())
				return false;
			return true;
		}

		private void InstantiateImplementation(ContainerService.Builder builder)
		{
			if (builder.DontUse())
			{
				builder.SetComment("DontUse");
				return;
			}
			var result = FactoryCreator.TryCreate(builder) ?? LazyCreator.TryCreate(builder);
			if (result != null)
			{
				builder.AddInstance(result, true);
				return;
			}
			if (NestedFactoryCreator.TryCreate(builder))
				return;
			var constructor = builder.Type.GetConstructor();
			if (!constructor.isOk)
			{
				builder.SetError(constructor.errorMessage);
				return;
			}
			var formalParameters = constructor.value.GetParameters();
			var actualArguments = new object[formalParameters.Length];
			var serviceNameParameterIndex = -1;
			for (var i = 0; i < formalParameters.Length; i++)
			{
				var formalParameter = formalParameters[i];
				if (formalParameter.ParameterType == typeof (ServiceName))
				{
					serviceNameParameterIndex = i;
					continue;
				}
				var dependency = InstantiateDependency(formalParameter, builder).CastTo(formalParameter.ParameterType);
				builder.AddDependency(dependency, false);
				if (dependency.ContainerService != null)
					builder.UnionUsedContracts(dependency.ContainerService);
				if (builder.Status != ServiceStatus.Ok)
					return;
				actualArguments[i] = dependency.Value;
			}
			builder.EndResolveDependencies();
			var dependenciesResolvedByArguments = builder.Arguments == null
				? InternalHelpers.emptyStrings
				: builder.Arguments.GetUsed().Select(InternalHelpers.ByNameDependencyKey);
			var unusedConfigurationKeys = builder.Configuration.GetUnusedDependencyConfigurationKeys()
				.Except(dependenciesResolvedByArguments)
				.ToArray();
			if (unusedConfigurationKeys.Length > 0)
			{
				builder.SetError(string.Format("unused dependency configurations [{0}]", unusedConfigurationKeys.JoinStrings(",")));
				return;
			}
			if (serviceNameParameterIndex >= 0)
				actualArguments[serviceNameParameterIndex] = builder.GetName();
			if (builder.CreateNew || builder.DeclaredContracts.Length == builder.FinalUsedContracts.Length)
			{
				builder.CreateInstance(constructor.value, null, actualArguments);
				return;
			}
			var serviceForUsedContractsId = instanceCache.GetOrAdd(builder.GetName(), createWrap);
			ContainerService serviceForUsedContracts;
			if (serviceForUsedContractsId.AcquireInstantiateLock(out serviceForUsedContracts))
			{
				builder.CreateInstance(constructor.value, null, actualArguments);
				serviceForUsedContracts = builder.Build();
				serviceForUsedContractsId.ReleaseInstantiateLock(serviceForUsedContracts);
			}
			else
				builder.Reuse(serviceForUsedContracts);
		}

		private ServiceDependency InstantiateDependency(ParameterInfo formalParameter, ContainerService.Builder builder)
		{
			object actualArgument;
			if (builder.Arguments != null && builder.Arguments.TryGet(formalParameter.Name, out actualArgument))
				return ServiceDependency.Constant(formalParameter, actualArgument);
			var parameters = builder.Configuration.ParametersSource;
			if (parameters != null && parameters.TryGet(formalParameter.Name, formalParameter.ParameterType, out actualArgument))
				return ServiceDependency.Constant(formalParameter, actualArgument);
			var dependencyConfiguration = builder.Configuration.GetOrNull(formalParameter);
			Type implementationType = null;
			if (dependencyConfiguration != null)
			{
				if (dependencyConfiguration.ValueAssigned)
					return ServiceDependency.Constant(formalParameter, dependencyConfiguration.Value);
				if (dependencyConfiguration.Factory != null)
					return ServiceDependency.Constant(formalParameter, dependencyConfiguration.Factory(this));
				implementationType = dependencyConfiguration.ImplementationType;
			}
			implementationType = implementationType ?? formalParameter.ParameterType;
			FromResourceAttribute resourceAttribute;
			if (implementationType == typeof (Stream) && formalParameter.TryGetCustomAttribute(out resourceAttribute))
			{
				var resourceStream = builder.Type.Assembly.GetManifestResourceStream(builder.Type, resourceAttribute.Name);
				if (resourceStream == null)
					return ServiceDependency.Error(null, formalParameter.Name,
						"can't find resource [{0}] in namespace of [{1}], assembly [{2}]",
						resourceAttribute.Name, builder.Type, builder.Type.Assembly.GetName().Name);
				return ServiceDependency.Constant(formalParameter, resourceStream);
			}
			var dependencyType = implementationType.UnwrapEnumerable();
			var isEnumerable = dependencyType != implementationType;
			var attribute = formalParameter.GetCustomAttributeOrNull<RequireContractAttribute>();
			var contracts = attribute == null ? null : new List<string>(1) {attribute.ContractName};

			ServiceConfiguration interfaceConfiguration;
			try
			{
				interfaceConfiguration = GetConfiguration(dependencyType, builder.Context);
			}
			catch (Exception e)
			{
				var dependencyService = new ContainerService.Builder(dependencyType, builder.Context, false, null);
				dependencyService.SetError(e);
				return ServiceDependency.ServiceError(dependencyService.Build());
			}
			if (interfaceConfiguration.FactoryWithTarget != null)
			{
				if (contracts == null)
					contracts = new List<string>();
				contracts.Add(builder.Type.FormatName());
			}
			if (dependencyType.IsSimpleType())
			{
				if (!formalParameter.HasDefaultValue)
					return ServiceDependency.Error(null, formalParameter.Name,
						"parameter [{0}] of service [{1}] is not configured",
						formalParameter.Name, builder.Type.FormatName());
				return ServiceDependency.Constant(formalParameter, formalParameter.DefaultValue);
			}
			var resultService = builder.Context.Resolve(dependencyType, contracts);
			if (resultService.Status.IsBad())
				return ServiceDependency.ServiceError(resultService);
			if (isEnumerable)
				return ServiceDependency.Service(resultService, resultService.GetAllValues());
			if (resultService.Status == ServiceStatus.NotResolved)
			{
				if (formalParameter.HasDefaultValue)
					return ServiceDependency.Service(resultService, formalParameter.DefaultValue);
				if (formalParameter.IsDefined<OptionalAttribute>() || formalParameter.IsDefined("CanBeNullAttribute"))
					return ServiceDependency.Service(resultService, null);
				return ServiceDependency.NotResolved(resultService);
			}
			return resultService.AsSingleInstanceDependency(null);
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			var exceptions = new List<SimpleContainerException>();
			foreach (var disposable in GetInstanceCache(typeof (IDisposable)).Reverse())
			{
				try
				{
					DisposeService(disposable);
				}
				catch (SimpleContainerException e)
				{
					exceptions.Add(e);
				}
			}
			if (exceptions.Count > 0)
			{
				var error = new AggregateException("SimpleContainer dispose error", exceptions);
				if (errorLogger == null)
					throw error;
				errorLogger(error.Message, error);
			}
		}

		private static void DisposeService(NamedInstance disposable)
		{
			try
			{
				((IDisposable) disposable.Instance).Dispose();
			}
			catch (Exception e)
			{
				if (e is OperationCanceledException)
					return;
				var aggregateException = e as AggregateException;
				if (aggregateException != null)
					if (aggregateException.Flatten().InnerExceptions.All(x => x is OperationCanceledException))
						return;
				var message = string.Format("error disposing [{0}]", disposable.Name);
				throw new SimpleContainerException(message, e);
			}
		}

		protected void EnsureNotDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException("SimpleContainer");
		}
	}
}