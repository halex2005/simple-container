using System.Collections.Generic;
using System.Linq;
using SimpleContainer.Helpers;
using SimpleContainer.Infection;

namespace SimpleContainer.Configuration
{
	public class ServiceConfigurationBuilder<T> :
		AbstractServiceConfigurationBuilder<ServiceConfigurationBuilder<T>, T>
	{
		internal ServiceConfigurationBuilder(ServiceConfigurationSet configurationSet)
			: base(configurationSet, new List<string>())
		{
		}

		public ServiceContractConfigurationBuilder<T> Contract<TContract>() where TContract : RequireContractAttribute, new()
		{
			return Contract(InternalHelpers.NameOf<TContract>());
		}

		public ServiceContractConfigurationBuilder<T> Contract(params string[] newContracts)
		{
			return new ServiceContractConfigurationBuilder<T>(configurationSet, contracts.Concat(newContracts.ToList()));
		}
	}
}