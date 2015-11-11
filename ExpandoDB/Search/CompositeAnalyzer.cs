﻿using FlexLucene.Analysis;
using FlexLucene.Analysis.Core;
using System;
using System.Collections.Concurrent;

namespace ExpandoDB.Search
{
    /// <summary>
    /// 
    /// </summary>
    public class CompositeAnalyzer : AnalyzerWrapper
    {
        private readonly ConcurrentDictionary<string, Analyzer> _perFieldAnalyzers;        
        private readonly Analyzer _textAnalyzer;
        private readonly Analyzer _keywordAnalyzer;
        private readonly IndexSchema _indexSchema;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeAnalyzer" /> class.
        /// </summary>
        /// <param name="indexSchema">The index schema.</param>
        /// <exception cref="ArgumentNullException">indexSchema</exception>
        public CompositeAnalyzer(IndexSchema indexSchema) :
            base(Analyzer.PER_FIELD_REUSE_STRATEGY)
        {
            if (indexSchema == null)
                throw new ArgumentNullException("indexSchema");
                   
            _textAnalyzer = new FullTextAnalyzer();
            _keywordAnalyzer = new KeywordAnalyzer();
            _indexSchema = indexSchema;

            _perFieldAnalyzers = new ConcurrentDictionary<string, Analyzer>();
            InitializePerFieldAnalyzers();            
        }

        private void InitializePerFieldAnalyzers()
        {   
            foreach (var fieldName in _indexSchema.IndexedFields.Keys)
            {                
                if (_perFieldAnalyzers.ContainsKey(fieldName))
                    continue;

                var indexedField = _indexSchema.IndexedFields[fieldName];
                switch (indexedField.DataType)
                {
                    case IndexedFieldDataType.String:
                    case IndexedFieldDataType.Number:
                    case IndexedFieldDataType.DateTime:
                        _perFieldAnalyzers[fieldName] = _keywordAnalyzer;
                        break;
                    default:
                        _perFieldAnalyzers[fieldName] = _textAnalyzer;
                        break;
                }                  
            }            
        }

        /// <summary>
        /// Gets the wrapped analyzer.
        /// </summary>
        /// <param name="fieldName">Name of the field.</param>
        /// <returns></returns>
        protected override Analyzer getWrappedAnalyzer(string fieldName)
        {
            var analyzer = _textAnalyzer;

            if (_perFieldAnalyzers.ContainsKey(fieldName))
                analyzer = _perFieldAnalyzers[fieldName];

            // Check if fieldName is new; if yes, then add it to the _perFieldAnalyzers
            if (_indexSchema.IndexedFields.ContainsKey(fieldName))
            {
                var indexedField = _indexSchema.IndexedFields[fieldName];
                switch (indexedField.DataType)
                {
                    case IndexedFieldDataType.String:
                    case IndexedFieldDataType.Number:
                    case IndexedFieldDataType.DateTime:
                        _perFieldAnalyzers[fieldName] = analyzer = _keywordAnalyzer;                        
                        break;
                    default:
                        _perFieldAnalyzers[fieldName] = analyzer = _textAnalyzer;
                        break;
                }       
            }

            return analyzer;
        }
    }
}

