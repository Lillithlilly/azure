﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Azure.Core.TestFramework;
using Azure.Data.Tables;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.WebJobs.Host.Indexers;
using NUnit.Framework;

namespace Microsoft.Azure.WebJobs.Extensions.Tables.Tests
{
    public class TableTests: TablesLiveTestBase
    {
        public TableTests(bool isAsync, bool useCosmos) : base(isAsync, useCosmos)
        {
        }

        [RecordedTest]
        public async Task Table_IndexingFails()
        {
            async Task AssertIndexingError<TProgram>(string methodName, string expectedErrorMessage)
            {
                try
                {
                    // Indexing is lazy, so must actually try a call first.
                    await CallAsync<TProgram>(methodName);
                }
                catch (FunctionIndexingException e)
                {
                    string functionName = typeof(TProgram).Name + "." + methodName;
                    Assert.AreEqual("Error indexing method '" + functionName + "'", e.Message);
                    StringAssert.Contains(expectedErrorMessage, e.InnerException.Message);
                    return;
                }
                Assert.Fail("Invoker should have failed");
            }

            // Verify we catch various indexing failures.
            await AssertIndexingError<BadProgramTableName>(nameof(BadProgramTableName.Run), "Validation failed for property 'TableName', value '$$'");
            // Pocos must have a default ctor.
            await AssertIndexingError<BadProgram4>(nameof(BadProgram4.Run), "Table entity types must provide a default constructor.");
        }

        private class CustomTableBindingConverter<T>
            : IConverter<TableClient, CustomTableBinding<T>>
        {
            public CustomTableBinding<T> Convert(TableClient input)
            {
                return new CustomTableBinding<T>(input);
            }
        }

        [RecordedTest]
        public async Task Table_IfBoundToCustomTableBindingExtension_BindsCorrectly()
        {
            // Arrange
            var ext = new TableConverterExtensionConfigProvider();
            await CallAsync<CustomTableBindingExtensionProgram>(configure: hostBuilder =>
            {
                DefaultConfigure(hostBuilder);
                hostBuilder.ConfigureWebJobs(builder => { builder.AddExtension(ext); });
            });

            // Assert
            Assert.AreEqual(TableName, CustomTableBinding<Poco>.Table.Name);
            Assert.True(CustomTableBinding<Poco>.AddInvoked);
            Assert.True(CustomTableBinding<Poco>.DeleteInvoked);
        }

        // Add a rule for binding TableClient --> CustomTableBinding<TEntity>
        internal class TableConverterExtensionConfigProvider : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                context.AddBindingRule<TableAttribute>().AddOpenConverter<TableClient, CustomTableBinding<OpenType>>(
                    typeof(CustomTableBindingConverter<>));
            }
        }

        private class CustomTableBindingExtensionProgram
        {
            public static void Run([Table(TableNameExpression)] CustomTableBinding<Poco> table)
            {
                Poco entity = new Poco();
                table.Add(entity);
                table.Delete(entity);
            }
        }

        private class BadProgramTableName
        {
            public static void Run([Table("$$")] ICollector<Poco> output)
            {
                Assert.Fail("should have gotten error at indexing time.");
            }
        }

        private class BadProgram4
        {
            public static void Run([Table(TableNameExpression, PartitionKey, RowKey)] BadPocoMissingDefaultCtor input)
            {
                Assert.Fail("should have gotten error at indexing time.");
            }
        }

        private class BadPocoMissingDefaultCtor
        {
            public BadPocoMissingDefaultCtor(string value)
            {
                this.Value = value;
            }

            public string Value { get; set; }
        }

        private class Poco
        {
            public string RowKey { get; set; }
            public string PartitionKey { get; set; }
            public string Property { get; set; }
        }

        /// <summary>
        /// Binding type demonstrating how custom binding extensions can be used to bind to
        /// arbitrary types
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        public class CustomTableBinding<TEntity>
        {
            public static bool AddInvoked;
            public static bool DeleteInvoked;
            public static TableClient Table;

            public CustomTableBinding(TableClient table)
            {
                // this custom binding has the table, so can perform whatever storage
                // operations it needs to
                Table = table;
            }

            public void Add(TEntity entity)
            {
                // storage operations here
                AddInvoked = true;
            }

            public void Delete(TEntity entity)
            {
                // storage operations here
                DeleteInvoked = true;
            }

            internal Task FlushAsync(CancellationToken cancellationToken)
            {
                // complete and flush all storage operations
                return Task.FromResult(true);
            }
        }
    }
}