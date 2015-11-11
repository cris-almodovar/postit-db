﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpandoDB.Search
{
    public class SearchResult<TResult>
    {
        public string Query { get; set; }
        public string SortByField { get; set; }
        public int? TopN { get; set; }
        public int? ItemsPerPage { get; set; }
        public int? PageNumber { get; set; }
        public int? HitCount { get; set; }
        public int? TotalHitCount { get; set; }
        public int? PageCount { get; set; }        
        public IEnumerable<TResult> Items { get; set; }       

        public SearchResult(SearchCriteria criteria)
        {
            Query = criteria.Query;
            SortByField = criteria.SortByField;
            TopN = criteria.TopN;
            ItemsPerPage = criteria.ItemsPerPage;
            PageNumber = criteria.PageNumber;

            HitCount = 0;
            TotalHitCount = 0;
            PageCount = 0;
            Items = new List<TResult>();
        }

        
    }
}
