// -----------------------------------------------------------------------
//  <copyright file="ConfigurationTests.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Raven.Abstractions.Extensions;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Database.FileSystem.Util;

using Xunit;
using Raven.Abstractions;
using Raven.Database.Config.Attributes;
using Raven.Database.Config.Settings;

namespace Raven.Tests.Core.Configuration
{
    public class ConfigurationTests
    {
        [Fact]
        public void NotChangingWorkingDirectoryShouldNotImpactPaths()
        {
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Initialize();

            var basePath = FilePathTools.MakeSureEndsWithSlash(AppDomain.CurrentDomain.BaseDirectory.ToFullPath());
            var workingDirectory = inMemoryConfiguration.Core.WorkingDirectory;

            Assert.Equal(basePath, workingDirectory);
            Assert.True(inMemoryConfiguration.Core.AssembliesDirectory.StartsWith(basePath));
            Assert.True(inMemoryConfiguration.Core.CompiledIndexCacheDirectory.StartsWith(basePath));
            Assert.True(inMemoryConfiguration.Core.DataDirectory.StartsWith(basePath));
            Assert.True(inMemoryConfiguration.FileSystem.DataDirectory.StartsWith(basePath));
            Assert.True(inMemoryConfiguration.Counter.DataDirectory.StartsWith(basePath));
            Assert.True(inMemoryConfiguration.TimeSeries.DataDirectory.StartsWith(basePath));
        }

        [Fact]
        public void ChangingWorkingDirectoryShouldImpactPaths()
        {
            string WorkingDirectoryValue = "C:\\Raven\\";
            if (EnvironmentUtils.RunningOnPosix == true)
                WorkingDirectoryValue = Environment.GetEnvironmentVariable("HOME") + @"\";
            
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Settings["Raven/WorkingDir"] = WorkingDirectoryValue;
            inMemoryConfiguration.Initialize();

            var basePath = FilePathTools.MakeSureEndsWithSlash(AppDomain.CurrentDomain.BaseDirectory.ToFullPath());
            var workingDirectory = inMemoryConfiguration.Core.WorkingDirectory;

            Assert.Equal(WorkingDirectoryValue, inMemoryConfiguration.Core.WorkingDirectory);
            Assert.NotEqual(basePath, workingDirectory);
            Assert.True(inMemoryConfiguration.Core.AssembliesDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.Core.CompiledIndexCacheDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.Core.DataDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.FileSystem.DataDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.Counter.DataDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.TimeSeries.DataDirectory.StartsWith(WorkingDirectoryValue));
        }

        [Fact]
        public void ChangingWorkingDirectoryShouldImpactRelativePaths()
        {
            string WorkingDirectoryValue = "C:\\Raven\\";
            if (EnvironmentUtils.RunningOnPosix == true)
                WorkingDirectoryValue = Environment.GetEnvironmentVariable("HOME") + @"\";
            
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Settings["Raven/WorkingDir"] = WorkingDirectoryValue;
            inMemoryConfiguration.Settings["Raven/AssembliesDirectory"] = "./my-assemblies";
            inMemoryConfiguration.Settings[InMemoryRavenConfiguration.GetKey(x => x.FileSystem.DataDirectory)] = "my-files";
            inMemoryConfiguration.Initialize();

            var basePath = FilePathTools.MakeSureEndsWithSlash(AppDomain.CurrentDomain.BaseDirectory.ToFullPath());
            var workingDirectory = inMemoryConfiguration.Core.WorkingDirectory;

            Assert.Equal(WorkingDirectoryValue, inMemoryConfiguration.Core.WorkingDirectory);
            Assert.NotEqual(basePath, workingDirectory);
            Assert.True(inMemoryConfiguration.Core.AssembliesDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.Core.CompiledIndexCacheDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.Core.DataDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.FileSystem.DataDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.Counter.DataDirectory.StartsWith(WorkingDirectoryValue));
            Assert.True(inMemoryConfiguration.TimeSeries.DataDirectory.StartsWith(WorkingDirectoryValue));
        }

        [Fact]
        public void ChangingWorkingDirectoryShouldNotImpactUNCPaths()
        {
            string WorkingDirectoryValue = "C:\\Raven\\";
            if (EnvironmentUtils.RunningOnPosix == true)
                WorkingDirectoryValue = Environment.GetEnvironmentVariable("HOME") + @"\";
            
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Settings["Raven/WorkingDir"] = WorkingDirectoryValue;
            inMemoryConfiguration.Settings["Raven/DataDir"] = @"\\server1\ravendb\data";
            inMemoryConfiguration.Settings[InMemoryRavenConfiguration.GetKey(x => x.FileSystem.DataDirectory)] = @"\\server1\ravenfs\data";
            inMemoryConfiguration.Settings[InMemoryRavenConfiguration.GetKey(x => x.Counter.DataDirectory)] = @"\\server1\ravenfs\data";
            inMemoryConfiguration.Settings[InMemoryRavenConfiguration.GetKey(x => x.TimeSeries.DataDirectory)] = @"\\server1\ravenfs\data";
            inMemoryConfiguration.Initialize();

            var basePath = FilePathTools.MakeSureEndsWithSlash(AppDomain.CurrentDomain.BaseDirectory.ToFullPath());
            var workingDirectory = inMemoryConfiguration.Core.WorkingDirectory;

            Assert.Equal(WorkingDirectoryValue, inMemoryConfiguration.Core.WorkingDirectory);
            Assert.NotEqual(basePath, workingDirectory);
            if (EnvironmentUtils.RunningOnPosix)
            {
                Assert.True(inMemoryConfiguration.Core.DataDirectory.StartsWith(@"/"));
                Assert.True(inMemoryConfiguration.FileSystem.DataDirectory.StartsWith(@"/"));
                Assert.True(inMemoryConfiguration.Counter.DataDirectory.StartsWith(@"/"));
                Assert.True(inMemoryConfiguration.TimeSeries.DataDirectory.StartsWith(@"/"));
            }
            else
            {
                Assert.True(inMemoryConfiguration.Core.DataDirectory.StartsWith(@"\\"));
                Assert.True(inMemoryConfiguration.FileSystem.DataDirectory.StartsWith(@"\\"));
                Assert.True(inMemoryConfiguration.Counter.DataDirectory.StartsWith(@"\\"));
                Assert.True(inMemoryConfiguration.TimeSeries.DataDirectory.StartsWith(@"\\"));
            }
        }

        [Fact]
        public void CanUseAppDrivePrefixInWorkingDirectoryForAutoDriveLetterCalculations()
        {
            const string WorkingDirectoryValue = "appDrive:\\Raven\\";
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Settings["Raven/WorkingDir"] = WorkingDirectoryValue;
            inMemoryConfiguration.Initialize();

            var basePath = FilePathTools.MakeSureEndsWithSlash(AppDomain.CurrentDomain.BaseDirectory.ToFullPath());
            var rootPath = Path.GetPathRoot(basePath);
            var workingDirectory = inMemoryConfiguration.Core.WorkingDirectory;

            Assert.NotEqual(basePath, workingDirectory);
            Assert.True(workingDirectory.StartsWith(rootPath));
        }

        [Fact]
        public void SizeSettingsMustHaveSizeUnitSpecified()
        {
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Initialize();

            var sizeSettings = ConfigurationExtractor.GetConfigurationItems(inMemoryConfiguration).Where(x => x.PropertyInfo.PropertyType == Size.TypeOf || x.PropertyInfo.PropertyType == Size.NullableTypeOf);

            foreach (var sizeSetting in sizeSettings)
            {
                Assert.NotNull(sizeSetting.PropertyInfo.GetCustomAttribute<SizeUnitAttribute>());
            }
        }

        [Fact]
        public void TimeSettingsMustHaveTimeUnitSpecified()
        {
            var inMemoryConfiguration = new InMemoryRavenConfiguration();
            inMemoryConfiguration.Initialize();

            var timeSettings = ConfigurationExtractor.GetConfigurationItems(inMemoryConfiguration).Where(x => x.PropertyInfo.PropertyType == TimeSetting.TypeOf || x.PropertyInfo.PropertyType == TimeSetting.NullableTypeOf);

            foreach (var timeSetting in timeSettings)
            {
                Assert.NotNull(timeSetting.PropertyInfo.GetCustomAttribute<TimeUnitAttribute>());
            }
        }

        [Fact]
        public void DefaultValuesRespectMinSizes()
        {
            var sut = new InMemoryRavenConfiguration();
            sut.Initialize();

            var configurationsWithMinValue = ConfigurationExtractor.GetConfigurationItems(sut).Where(x => x.PropertyInfo.GetCustomAttribute<MinValueAttribute>() != null);

            foreach (var item in configurationsWithMinValue)
            {
                var minValue = item.PropertyInfo.GetCustomAttribute<MinValueAttribute>().Int32Value;

                if (item.PropertyInfo.PropertyType == typeof(int))
                {
                    Assert.True((int)item.Value >= minValue);
                }
                else if (item.PropertyInfo.PropertyType == typeof(int?))
                {
                    if (item.Value == null)
                        continue;

                    Assert.True((int?)item.Value >= minValue);
                }
                else if (item.PropertyInfo.PropertyType == typeof(Size))
                {
                    Assert.True((Size)item.Value >= new Size(minValue, item.PropertyInfo.GetCustomAttribute<SizeUnitAttribute>().Unit), "Default smaller than min value. Property name:" + item.PropertyInfo.Name);
                }
                else
                {
                   Assert.True(false, "Unknown min configuration value type:" + item.PropertyInfo.PropertyType);
                }
            }
        }

        [Fact]
        public void CannotBeSmallerThanGivenMinValueEvenIfSmallerValueWasSpecified()
        {
            var fake = new InMemoryRavenConfiguration();
            fake.Initialize();

            var keys = ConfigurationExtractor.GetConfigurationItems(fake).Where(x => x.PropertyInfo.GetCustomAttribute<MinValueAttribute>() != null).Select(c => c.Key);

            var sut = new InMemoryRavenConfiguration();

            foreach (var key in keys)
            {
                sut.Settings.Add(key, "0");
            }

            sut.Initialize();

            var configurationsWithMinValue = ConfigurationExtractor.GetConfigurationItems(sut).Where(x => x.PropertyInfo.GetCustomAttribute<MinValueAttribute>() != null).ToList();

            foreach (var item in configurationsWithMinValue)
            {
                var minValue = item.PropertyInfo.GetCustomAttribute<MinValueAttribute>().Int32Value;

                if (item.PropertyInfo.PropertyType == typeof(int))
                {
                    Assert.True((int)item.Value >= minValue);
                }
                else if (item.PropertyInfo.PropertyType == typeof(int?))
                {
                    if (item.Value == null)
                        continue;

                    Assert.True((int?)item.Value >= minValue);
                }
                else if (item.PropertyInfo.PropertyType == typeof(Size))
                {
                    Assert.True((Size)item.Value >= new Size(minValue, item.PropertyInfo.GetCustomAttribute<SizeUnitAttribute>().Unit), "Specified value is smaller than min value. Property name: " + item.PropertyInfo.Name);
                }
                else
                {
                    Assert.True(false, "Unknown min configuration value type:" + item.PropertyInfo.PropertyType);
                }
            }
        }

        [Fact]
        public void SettingNonDefaultEnumSettings()
        {
            var fake = new InMemoryRavenConfiguration();
            fake.Initialize();

            var enumItems = ConfigurationExtractor.GetConfigurationItems(fake).Where(x => x.PropertyInfo.PropertyType.IsEnum).ToArray();

            var sut = new InMemoryRavenConfiguration();

            var expectedEnumValues = new object[enumItems.Length];

            var random = new Random();

            for (int i = 0; i < enumItems.Length; i++)
            {
                var values = Enum.GetValues(enumItems[i].PropertyInfo.PropertyType).OfType<object>().Except(new [] { enumItems[i].PropertyInfo.GetCustomAttribute<DefaultValueAttribute>().Value }).ToArray();
                expectedEnumValues[i] = values.GetValue(random.Next(values.Length));

                sut.Settings.Add(enumItems[i].Key, expectedEnumValues[i].ToString());
            }
            
            sut.Initialize();

            var actual = ConfigurationExtractor.GetConfigurationItems(sut).Where(x => x.PropertyInfo.PropertyType.IsEnum).ToArray();

            for (int i = 0; i < actual.Length; i++)
            {
                Assert.Equal(expectedEnumValues[i], actual[i].Value);
            }
        }
        
        [Fact]
        public void DefaultConfigurationHasDefaultValues()
        {
            var sut = new InMemoryRavenConfiguration();
            sut.Initialize();

            var configurations = ConfigurationExtractor.GetConfigurationItems(sut).ToList();

            foreach (var configuration in configurations)
            {
                var expected = configuration.PropertyInfo.GetCustomAttribute<DefaultValueAttribute>().Value;

                if (InMemoryRavenConfiguration.ConfigurationBase.DefaultValueSetInConstructor.Equals(expected))
                    continue; // cannot verify default values set in ctor automatically

                if (expected == null)
                    continue; // nulls are usually used for custom logic, e.g. Core.IndexStoragePath

                var expectedStringValue = expected as string;

                if (expectedStringValue != null && (expectedStringValue.StartsWith(@"~\") || "".Equals(expectedStringValue)))
                    continue; // we have separate tests for paths
                
                if (configuration.PropertyInfo.PropertyType == Size.TypeOf)
                {
                    Assert.Equal(new Size(Convert.ToInt64(expected), configuration.PropertyInfo.GetCustomAttribute<SizeUnitAttribute>().Unit), configuration.Value);
                }
                else if (configuration.PropertyInfo.PropertyType == TimeSetting.TypeOf)
                {
                    Assert.Equal(new TimeSetting(Convert.ToInt64(expected), configuration.PropertyInfo.GetCustomAttribute<TimeUnitAttribute>().Unit), configuration.Value);
                }
                else
                {
                    Assert.Equal(expected, configuration.Value);
                }
            }
        }

        [Fact]
        public void AllConfigurationsHaveDefaultValueAttribute()
        {
            var sut = new InMemoryRavenConfiguration();
            sut.Initialize();

            var configurations = ConfigurationExtractor.GetConfigurationItems(sut).ToList();

            foreach (var configuration in configurations)
            {
                Assert.NotNull(configuration.PropertyInfo.GetCustomAttribute<DefaultValueAttribute>());
            }
        }

        private class ConfigurationExtractor
        {
            public static List<ConfigurationItem> GetConfigurationItems(InMemoryRavenConfiguration inMemoryConfiguration)
            {
                var configurationClasses = inMemoryConfiguration.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Type().BaseType == typeof(InMemoryRavenConfiguration.ConfigurationBase));

                var result = new List<ConfigurationItem>();

                foreach (var configurationClass in configurationClasses)
                {
                    var configurationClassInstance = configurationClass.GetValue(inMemoryConfiguration);

                    foreach (var configuration in configurationClassInstance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.GetCustomAttributes<ConfigurationEntryAttribute>().Any()))
                    {
                        result.Add(new ConfigurationItem
                        {
                            PropertyInfo = configuration,
                            Value = configuration.GetValue(configurationClassInstance),
                            Key = configuration.GetCustomAttributes<ConfigurationEntryAttribute>().First().Key
                    });
                    }
                    
                }

                return result;
            }
        }

        private class ConfigurationItem
        {
            public PropertyInfo PropertyInfo;
            public object Value;
            public string Key;
        }
    }
}
