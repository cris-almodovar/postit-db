﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpandoDB.Storage
{
    /// <summary>
    /// A Schema Storage engine that persists data using the LightningDB key-value store.
    /// </summary>
    /// <seealso cref="ExpandoDB.Storage.ISchemaStorage" />
    public class LightningSchemaStorage : ISchemaStorage
    {
        private const string _database = "__schema__";
        private readonly LightningStorageEngine _storageEngine;

        public LightningSchemaStorage(LightningStorageEngine storageEngine)
        {            
            _storageEngine = storageEngine;
            _storageEngine.InitializeDatabase(_database);
        }        

        public async Task<DocumentCollectionSchema> GetAsync(string schemaName)
        {
            if (String.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException($"{nameof(schemaName)} cannot be null or blank");

            var key = schemaName.ToByteArray();
            var kv = await _storageEngine.GetAsync(_database, key).ConfigureAwait(false);

            return kv.ToDocumentCollectionSchema();
        }

        public async Task<IList<DocumentCollectionSchema>> GetAllAsync()
        {
            var allCollectionSchemas = new List<DocumentCollectionSchema>();            
            var allKv = await _storageEngine.GetAllAsync(_database).ConfigureAwait(false);

            foreach (var kv in allKv)
            {
                var collectionSchema = kv.ToDocumentCollectionSchema();
                allCollectionSchemas.Add(collectionSchema);
            }            

            return allCollectionSchemas;
        }

        public async Task<string> InsertAsync(DocumentCollectionSchema collectionSchema)
        {
            if (collectionSchema == null)
                throw new ArgumentNullException(nameof(collectionSchema));
            
            var name = collectionSchema.Name;
            var kv = collectionSchema.ToKeyValuePair();           

            await _storageEngine.InsertAsync(_database, kv).ConfigureAwait(false);

            return name;
            
        }

        public async Task<int> UpdateAsync(DocumentCollectionSchema collectionSchema)
        {
            if (collectionSchema == null)
                throw new ArgumentNullException(nameof(collectionSchema));
            
            var name = collectionSchema.Name;
            var kv = collectionSchema.ToKeyValuePair();
                
            var updatedCount = await _storageEngine.UpdateAsync(_database, kv).ConfigureAwait(false);
            return updatedCount;            
        }

        public async Task<int> DeleteAsync(string schemaName)
        {
            if (String.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException($"{nameof(schemaName)} cannot be null or blank");

            var key = schemaName.ToByteArray();
            var deletedCount = await _storageEngine.DeleteAsync(_database, key).ConfigureAwait(false);
            return deletedCount;
        }
    }
}
