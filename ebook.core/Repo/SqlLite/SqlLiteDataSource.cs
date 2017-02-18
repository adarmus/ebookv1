﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ebook.core.DataTypes;
using Insight.Database;

namespace ebook.core.Repo.SqlLite
{
    public class SqlLiteDataSource : IFullDataSource
    {
        readonly SQLiteConnectionStringBuilder  _builder;

        public SqlLiteDataSource(string dataFile)
        {
            _builder = new SQLiteConnectionStringBuilder() { DataSource = dataFile };
        }

        public async Task<IEnumerable<BookInfo>> GetBooks(bool includeMobi, bool includeEpub)
        {
            try
            {
                var books = await GetBookSqlDal().BookSelAll();

                return books;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        public async Task<BookFilesInfo> GetBookContent(BookInfo book)
        {
            try
            {
                IBookSqlLiteDal repo = GetBookSqlDal();

                BookInfo info = await repo.BookSelById(new Guid(book.Id));

                IEnumerable<BookFileInfo> files = await repo.BookFileSelByBookId(new Guid(book.Id));

                var content = new BookFilesInfo(info, files);

                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        public async Task SaveBook(BookInfo book)
        {
            await GetBookSqlDal().BookIns(book);
        }

        public async Task SaveFile(BookFileInfo file)
        {
            await GetBookSqlDal().FileIns(file);
        }

        public async Task<BookInfo> GetBookByIsbn(string isbn)
        {
            var book = await GetBookSqlDal().BookSelByIsbn(isbn);

            return book;
        }

        public async Task<BookInfo> GetBookByTitleAuthor(string title, string author)
        {
            var book = await GetBookSqlDal().BookSelByTitleAuthor(title, author);

            return book;
        }

        private IBookSqlLiteDal GetBookSqlDal()
        {
            var connection = new SQLiteConnection(_builder.ConnectionString);
            return connection.As<IBookSqlLiteDal>();
        }
    }
}
