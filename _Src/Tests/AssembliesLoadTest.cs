using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SimpleContainer.Tests.Helpers;

namespace SimpleContainer.Tests
{
	public abstract class AssembliesLoadTest : UnitTestBase
	{
		private AppDomain appDomain;
		private static readonly string testDirectory = Path.GetFullPath("testDirectory");

		protected override void SetUp()
		{
			base.SetUp();
			if (Directory.Exists(testDirectory))
				Directory.Delete(testDirectory, true);
			Directory.CreateDirectory(testDirectory);

			appDomain = AppDomain.CreateDomain("test", null, new AppDomainSetup {ApplicationBase = testDirectory});
		}

		protected override void TearDown()
		{
			if (appDomain != null)
				AppDomain.Unload(appDomain);
			if (Directory.Exists(testDirectory))
				Directory.Delete(testDirectory, true);
			base.TearDown();
		}

		private void CopyAssemblyToTestDirectory(Assembly assembly)
		{
			File.Copy(assembly.Location, Path.Combine(testDirectory, Path.GetFileName(assembly.Location)));
		}

		private FactoryInvoker GetInvoker()
		{
			return (FactoryInvoker) appDomain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().GetName().FullName,
				typeof (FactoryInvoker).FullName);
		}

		private class FactoryInvoker : MarshalByRefObject
		{
			public string CreateCointainerWithCrash()
			{
				try
				{
					CreateContainer();
					return "can't reach here";
				}
				catch (Exception e)
				{
					return e.ToString();
				}
			}

			private static void CreateContainer()
			{
				new ContainerFactory()
					.WithAssembliesFilter(x => x.Name.StartsWith("tmp_"))
					.WithTypesFromDefaultBinDirectory(false)
					.Build();
			}
		}

		public class CorrectExceptionHandling : AssembliesLoadTest
		{
			private const string referencedAssemblyCodeV1 = @"
					using SimpleContainer.Configuration;
					using SimpleContainer;
					using System;

					namespace A1
					{
						public interface ISomeInterface
						{
						}
					}
				";

			private const string referencedAssemblyCodeV2 = @"
					using SimpleContainer.Configuration;
					using SimpleContainer;
					using System;

					namespace A1
					{
						public interface ISomeInterface
						{
							void Do();
						}
					}
				";

			private const string primaryAssemblyCode = @"
					using System;
					using A1;

					namespace A2
					{
						public class TestClass: ISomeInterface
						{
							void ISomeInterface.Do()
							{
							}
						}
					}
				";


			[Test]
			public void Test()
			{
				var referencedAssemblyV2 = AssemblyCompiler.Compile(referencedAssemblyCodeV2);
				AssemblyCompiler.Compile(referencedAssemblyCodeV1,
					Path.Combine(testDirectory, Path.GetFileName(referencedAssemblyV2.Location)));
				var primaryAssembly = AssemblyCompiler.Compile(primaryAssemblyCode, referencedAssemblyV2);

				CopyAssemblyToTestDirectory(primaryAssembly);
				CopyAssemblyToTestDirectory(typeof (IContainer).Assembly);
				CopyAssemblyToTestDirectory(Assembly.GetExecutingAssembly());

				var exceptionText = GetInvoker().CreateCointainerWithCrash();
				Assert.That(exceptionText, Is.StringContaining("A1.ISomeInterface.Do"));

				const string englishText = "Unable to load one or more of the requested types";
				const string russianText = "�� ������� ��������� ���� ��� ����� ����������� �����";
				Assert.That(exceptionText, Is.StringContaining(englishText).Or.StringContaining(russianText));
				Assert.That(exceptionText, Is.StringContaining(primaryAssembly.GetName().Name));
			}
		}
	}
}