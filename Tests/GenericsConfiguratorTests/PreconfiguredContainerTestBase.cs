namespace SimpleContainer.Tests.GenericsConfiguratorTests
{
	public abstract class PreconfiguredContainerTestBase : SimpleContainerTestBase
	{
		protected SimpleContainer container;

		protected override void SetUp()
		{
			base.SetUp();
			container = Container();
		}
	}
}