﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ImportSearchParamStep : IStep
    {
        private const int MaxBatchSize = 100000;
        private const int ConcurrentLimit = 4;

        private Task _runningTask;
        private Dictionary<SearchParamType, List<BulkCopySearchParamWrapper>> _buffer;
        private Channel<BulkCopySearchParamWrapper> _input;
        private Dictionary<SearchParamType, ISearchParamGenerator> _generators;
        private ModelProvider _provider;
        private IConfiguration _configuration;
        private Queue<Task> _bulkCopyTasks = new Queue<Task>();

        public ImportSearchParamStep(
            Channel<BulkCopySearchParamWrapper> input,
            ModelProvider provider,
            IConfiguration configuration)
        {
            _buffer = new Dictionary<SearchParamType, List<BulkCopySearchParamWrapper>>();
            _input = input;
            _provider = provider;
            _configuration = configuration;

            InitializeSearchParamGenerator();
        }

        public void Start()
        {
            _runningTask = Task.Run(async () =>
            {
                while (await _input.Reader.WaitToReadAsync())
                {
                    await foreach (BulkCopySearchParamWrapper resource in _input.Reader.ReadAllAsync())
                    {
                        var parameterType = resource.SearchIndexEntry.SearchParameter.Type;
                        if (!_generators.ContainsKey(parameterType))
                        {
                            continue; // TODO: we should throw exception for not support later.
                        }

                        if (!_buffer.ContainsKey(parameterType))
                        {
                            _buffer[parameterType] = new List<BulkCopySearchParamWrapper>();
                        }

                        _buffer[parameterType].Add(resource);
                        if (_buffer[parameterType].Count < MaxBatchSize)
                        {
                            continue;
                        }

                        if (_bulkCopyTasks.Count >= ConcurrentLimit)
                        {
                            await _bulkCopyTasks.Dequeue();
                        }

                        BulkCopySearchParamWrapper[] items = _buffer[parameterType].ToArray();
                        _buffer[parameterType].Clear();

                        _bulkCopyTasks.Enqueue(BulkCopyToSearchParamTableAsync(parameterType, items));
                    }

                    foreach (var searchParams in _buffer)
                    {
                        _bulkCopyTasks.Enqueue(BulkCopyToSearchParamTableAsync(searchParams.Key, searchParams.Value.ToArray()));
                    }

                    Task.WaitAll(_bulkCopyTasks.ToArray());
                }
            });
        }

        public Task WaitForStopAsync()
        {
            throw new System.NotImplementedException();
        }

        private async Task BulkCopyToSearchParamTableAsync(SearchParamType parameterType, BulkCopySearchParamWrapper[] items)
        {
            ISearchParamGenerator generator = _generators[parameterType];
            using (SqlConnection destinationConnection =
                       new SqlConnection(_configuration["SqlConnectionString"]))
            {
                destinationConnection.Open();

                DataTable importTable = generator.CreateDataTable();
                foreach (var searchItem in items)
                {
                    importTable.Rows.Add(generator.GenerateDataRow(importTable, searchItem));
                }

                using IDataReader reader = importTable.CreateDataReader();
                using (SqlBulkCopy bulkCopy =
                            new SqlBulkCopy(destinationConnection))
                {
                    try
                    {
                        bulkCopy.DestinationTableName =
                            generator.TableName;
                        await bulkCopy.WriteToServerAsync(reader);

                        Console.WriteLine($"{items.Length} search params to db completed.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    finally
                    {
                        destinationConnection.Close();
                    }
                }
            }
        }

        private void InitializeSearchParamGenerator()
        {
            _generators = new Dictionary<SearchParamType, ISearchParamGenerator>()
            {
                { SearchParamType.String, new StringSearchParamGenerator(_provider) },
            };
        }
    }
}