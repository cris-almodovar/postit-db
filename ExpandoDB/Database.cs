﻿using Common.Logging;
using ExpandoDB.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ExpandoDB.Search;

namespace ExpandoDB
{
    /// <summary>
    /// Represents a collection of <see cref="Collection"/> objects. 
    /// </summary>
    /// <remarks>
    /// This class is analogous to a MongoDB database.
    /// </remarks>
    public class Database : IDisposable
    {
        internal const string DATA_DIRECTORY_NAME = "data";
        internal const string DB_DIRECTORY_NAME = "db";
        internal const string INDEX_DIRECTORY_NAME = "index";
        private readonly string _dataPath;                
        private readonly string _indexPath;
        private readonly IDictionary<string, Collection> _collections;
        private readonly LightningStorageEngine _storageEngine;
        private readonly LightningDocumentStorage _documentStorage;
        private readonly Timer _schemaPersistenceTimer;
        private readonly object _schemaPersistenceLock = new object();
        private readonly double _schemaPersistenceIntervalSeconds;
        private readonly ILog _log = LogManager.GetLogger(typeof(Database).Name);

        /// <summary>
        /// Initializes a new instance of the <see cref="Database" /> class.
        /// </summary>
        /// <param name="dataPath">The database directory path.</param>
        public Database(string dataPath = null)
        {
            if (String.IsNullOrWhiteSpace(dataPath))
            {
                var appPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                dataPath = Path.Combine(appPath, DATA_DIRECTORY_NAME);
            }

            _dataPath = dataPath;            
            EnsureDataDirectoryExists(_dataPath);
            _log.Info($"Data Path: {_dataPath}");

            _indexPath = Path.Combine(_dataPath, INDEX_DIRECTORY_NAME);
            EnsureIndexDirectoryExists(_indexPath);
            _log.Info($"Index Path: {_indexPath}");

            _storageEngine = new LightningStorageEngine(_dataPath);
            _documentStorage = new LightningDocumentStorage(_storageEngine);
            _collections = new Dictionary<string, Collection>();

            var persistedSchemas = _documentStorage.GetAllAsync(Schema.COLLECTION_NAME).Result.Select(d => new Schema().PopulateWith(d.AsDictionary()));  
            foreach (var schema in persistedSchemas)
            {
                var collection = new Collection(schema, _documentStorage);
                _collections.Add(schema.Name, collection);
            }

            _schemaPersistenceIntervalSeconds = Double.Parse(ConfigurationManager.AppSettings["Schema.PersistenceIntervalSeconds"] ?? "1");
            _schemaPersistenceTimer = new Timer(_ => Task.Run(async () => await PersistSchemas().ConfigureAwait(false)), null, TimeSpan.FromSeconds(_schemaPersistenceIntervalSeconds), TimeSpan.FromSeconds(_schemaPersistenceIntervalSeconds));
        }       

        /// <summary>
        /// Ensures the data directory exists.
        /// </summary>
        /// <param name="dataPath">The database path.</param>
        private static void EnsureDataDirectoryExists(string dataPath)
        {
            if (!Directory.Exists(dataPath))
                Directory.CreateDirectory(dataPath);            
        }

        /// <summary>
        /// Ensures the Lucene index directory exists.
        /// </summary>
        private static void EnsureIndexDirectoryExists(string indexPath)
        {
            if (!Directory.Exists(indexPath))
                Directory.CreateDirectory(indexPath);
        }

        /// <summary>
        /// Gets the <see cref="Collection"/> with the specified name.
        /// </summary>
        /// <value>
        /// The <see cref="Collection"/> with the specified name.
        /// </value>
        /// <param name="name">The name of the Document Collection.</param>
        /// <returns></returns>
        public Collection this [string name]
        {
            get
            {
                if (String.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("name cannot be null or blank");

                Collection collection = null;
                if (!_collections.ContainsKey(name))
                {
                    lock (_collections)
                    {
                        if (!_collections.ContainsKey(name))
                        {
                            collection = new Collection(name, _documentStorage);
                            _collections.Add(name, collection);
                        }
                    }
                }

                collection = _collections[name];
                if (collection == null || collection.IsDropped)
                    throw new InvalidOperationException($"The Document Collection '{name}' does not exist.");

                return collection;
            }
        }

        /// <summary>
        /// Gets the names of all Document Collections in the Database.
        /// </summary>
        /// <value>
        /// The name of all Document Collections in the Database.
        /// </value>
        public IEnumerable<string> GetCollectionNames()
        {
            return _collections.Keys;
        }

        /// <summary>
        /// Drops the Document Collection with the specified name.
        /// </summary>
        /// <param name="collectionName">The name of the Document Collection to drop.</param>
        /// <returns></returns>
        public async Task<bool> DropCollectionAsync(string collectionName)
        {
            if (!_collections.ContainsKey(collectionName))
                return false;

            var isSuccessful = false;
            Collection collection = null;
            
            lock (_collections)
            {
                if (_collections.ContainsKey(collectionName))
                {
                    collection = _collections[collectionName];
                    _collections.Remove(collectionName);                   
                }                    
            }

            if (collection == null)
                return false;
            
            isSuccessful = await collection.DropAsync().ConfigureAwait(false) &&
                           await _documentStorage.DeleteAsync(Schema.COLLECTION_NAME, collection.Schema._id).ConfigureAwait(false) == 1;

            return isSuccessful;
        }

        /// <summary>
        /// Determines whether the Database contains a Document Collection with the specified name.
        /// </summary>
        /// <param name="collectionName">The name of the Document Collection.</param>
        /// <returns></returns>
        public bool ContainsCollection(string collectionName)
        {
            return _collections.ContainsKey(collectionName);
        }

        private async Task PersistSchemas()
        {
            var isLockTaken = false;
            try
            {
                isLockTaken = Monitor.TryEnter(_schemaPersistenceLock, 500);
                if (!isLockTaken)
                    return;

                foreach (var collection in _collections.Values)
                {
                    if (collection.IsDropped || collection.IsDisposed)
                        continue;

                    await PersistSchema(collection).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
            finally
            {
                if (isLockTaken)
                    Monitor.Exit(_schemaPersistenceLock);
            }
        }

        private async Task PersistSchema(Collection collection)
        {
            try
            {
                var liveSchemaDocument = collection.Schema.ToDocument();
                var savedSchemaDocument = await _documentStorage.GetAsync(Schema.COLLECTION_NAME, collection.Schema._id);

                if (savedSchemaDocument == null)
                {
                    await _documentStorage.InsertAsync(Schema.COLLECTION_NAME, liveSchemaDocument);
                    savedSchemaDocument = await _documentStorage.GetAsync(Schema.COLLECTION_NAME, liveSchemaDocument._id.Value);
                    collection.Schema._createdTimestamp = savedSchemaDocument._createdTimestamp;
                    collection.Schema._modifiedTimestamp = savedSchemaDocument._modifiedTimestamp;
                }
                else
                {
                    if (liveSchemaDocument != savedSchemaDocument)
                        await _documentStorage.UpdateAsync(Schema.COLLECTION_NAME, liveSchemaDocument);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
        }

        #region IDisposable Support

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    _schemaPersistenceTimer.Dispose();

                    foreach (var collection in _collections.Values)
                        collection.Dispose();

                    _storageEngine.Dispose();
                }

                IsDisposed = true;
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {            
            Dispose(true);         
        }

        #endregion
    }
}
