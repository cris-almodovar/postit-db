﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExpandoDB.Server.Web.DTO
{
    public class SchemaResponseDto : IResponseDto
    {
        public string Elapsed { get; set; }
        public ContentCollectionSchema Schema { get; set; }
    }
}
