﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ebook.core.DataTypes
{
    public class BookFilesInfo
    {
        public BookInfo Book { get; set; }

        public IEnumerable<string> FileIds { get; }

        public IEnumerable<BookFileInfo> Files { get; }

        public BookFilesInfo(BookInfo book, IEnumerable<string> fileIds)
        {
            this.Book = book;
            this.FileIds = fileIds;
        }

        public BookFilesInfo(BookInfo book, IEnumerable<BookFileInfo> files)
        {
            this.Book = book;
            this.Files = files;
            this.FileIds = files.Select(f => f.FileName);
        }

        public BookFilesInfo(BookInfo book, string file)
        {
            this.Book = book;
            FileIds = new [] { file };
        }
    }
}
