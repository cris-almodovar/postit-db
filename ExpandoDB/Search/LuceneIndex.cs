﻿using FlexLucene.Analysis;
using FlexLucene.Index;
using FlexLucene.Queryparser.Classic;
using FlexLucene.Search;
using FlexLucene.Store;
using java.nio.file;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using LuceneFieldType = FlexLucene.Document.FieldType;
using System.Threading;

namespace ExpandoDB.Search
{
    /// <summary>
    /// Represents the Lucene full-text index for a Collection of dynamic Contents objects
    /// </summary>
    public class LuceneIndex : IDisposable
    {
        private const int DEFAULT_SEARCH_TOP_N = 100000;
        private const int DEFAULT_SEARCH_ITEMS_PER_PAGE = 10;
        private readonly Directory _indexDirectory;
        private readonly Analyzer _compositeAnalyzer;
        private readonly IndexWriter _writer;                
        private SearcherManager _searcherManager;
        private object _searcherManagerLock = new object();
        private readonly Timer _autoRefreshTimer;
        private readonly IndexSchema _indexSchema;
        public IndexSchema Schema { get { return _indexSchema; } }       


        /// <summary>
        /// Initializes a new instance of the <see cref="LuceneIndex"/> class.
        /// </summary>
        /// <param name="indexPath">The path to the directory that will contain the index files.</param>                
        public LuceneIndex(string indexPath, IndexSchema indexSchema = null)
        {
            if (String.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentNullException("indexPath");
            if (indexSchema == null)
                indexSchema = IndexSchema.CreateDefault();

            if (!System.IO.Directory.Exists(indexPath))
                System.IO.Directory.CreateDirectory(indexPath);

            var path = Paths.get(indexPath);
            _indexDirectory = new MMapDirectory(path);

            _indexSchema = indexSchema ?? IndexSchema.CreateDefault();
            _compositeAnalyzer = new CompositeAnalyzer(_indexSchema);

            var config = new IndexWriterConfig(_compositeAnalyzer);
            _writer = new IndexWriter(_indexDirectory, config);

            _searcherManager = new SearcherManager(_writer, true, null);            

            _autoRefreshTimer = new Timer(o => Refresh(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));         
        }        
        
        /// <summary>
        /// Refreshes the Lucene index so that Search() reflects the latest insertions and deletions.
        /// </summary>
        /// <remarks>
        /// The LuceneIndex auto-invokes this method automatically every 500 milliseconds.
        /// </remarks>
        public void Refresh()
        {
            try
            {
                _searcherManager.MaybeRefresh();
            }
            catch { }
        }        

        /// <summary>
        /// Inserts a dynamic content into the index.
        /// </summary>
        /// <param name="content">The dynamic content.</param>        
        public void Insert(Content content)
        {
            if (content == null)
                throw new ArgumentNullException("content");
            
            var document = content.ToLuceneDocument(_indexSchema);
            
            _writer.AddDocument(document);
            _writer.Commit();            
        }

        /// <summary>
        /// Deletes the content identified by the guid.
        /// </summary>
        /// <param name="guid">The unique identifier.</param>
        public void Delete(Guid guid)
        {
            if (guid == Guid.Empty)
                throw new ArgumentException("guid cannot be empty");

            var idTerm = new Term("_id", guid.ToString());
            _writer.DeleteDocuments(idTerm);
            _writer.Commit();
        }

        /// <summary>
        /// Updates the Content identified by its _id field.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <exception cref="ArgumentNullException">content</exception>
        public void Update(Content content)
        {
            if (content == null)
                throw new ArgumentNullException("content");

            if (content._id == null || content._id == Guid.Empty)
                throw new InvalidOperationException("Cannot update Content that does not have an _id");
            
            var document = content.ToLuceneDocument(_indexSchema);

            var id = content._id.ToString();
            var idTerm = new Term("_id", id);
            
            _writer.UpdateDocument(idTerm, document);
            _writer.Commit();
        }

        /// <summary>
        /// Searches the Lucene index for Contents that match the specified search criteria.
        /// </summary>
        /// <param name="criteria">The search criteria.</param>
        /// <returns></returns>
        public SearchResult<Guid> Search(SearchCriteria criteria)
        {
            if (criteria == null)
                throw new ArgumentNullException("criteria");

            criteria.Query = String.IsNullOrWhiteSpace(criteria.Query) ? "*:*" : criteria.Query;
            criteria.TopN = criteria.TopN > 0 ? criteria.TopN : DEFAULT_SEARCH_TOP_N;
            criteria.ItemsPerPage = criteria.ItemsPerPage > 0 ? criteria.ItemsPerPage : DEFAULT_SEARCH_ITEMS_PER_PAGE;
            criteria.PageNumber = criteria.PageNumber > 0 ? criteria.PageNumber : 1;
            criteria.Validate();

            var result = new SearchResult<Guid>(criteria);
            var queryParser = new LuceneQueryParser(LuceneField.FULL_TEXT_FIELD_NAME, _compositeAnalyzer, _indexSchema);
            var query = queryParser.Parse(criteria.Query);

            var searcher = _searcherManager.Acquire() as IndexSearcher;
            if (searcher != null)
            {
                try
                {
                    var sort = GetSortCriteria(criteria.SortByField);
                    var topFieldDocs = searcher.Search(query, criteria.TopN, sort);
                    result.PopulateWith(topFieldDocs, id => searcher.Doc(id));
                }
                finally
                {
                    _searcherManager.Release(searcher);
                    searcher = null;
                }
            }

            return result;
        }        

        private void InitializeSearcherManager()
        {
            lock (_searcherManagerLock)
            {
                _searcherManager = new SearcherManager(_writer, true, null);
            }
        }

        private Sort GetSortCriteria(string sortByField = null)
        {
            if (String.IsNullOrWhiteSpace(sortByField))
                return Sort.RELEVANCE;

            var sortFields = new List<SortField>();            
            var sortFieldName = sortByField.Trim();
            var reverse = sortFieldName.StartsWith("-", StringComparison.InvariantCulture);
            if (reverse)
                sortFieldName = sortFieldName.TrimStart('-');

            sortFields.Add(new SortField(sortFieldName, SortField.Type.STRING, reverse));            

            return new Sort(sortFields.ToArray());
        }       

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _searcherManager.Close();
            _writer.Close();
            _autoRefreshTimer.Dispose();            
        }
    }
}
