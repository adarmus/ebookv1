﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ebook.Async;
using ebook.core.DataTypes;
using ebook.core.Logic;
using ebook.core.Repo;
using GalaSoft.MvvmLight;
using log4net;
using log4net.Config;

namespace ebook
{
    internal class MainWindowViewModel : ViewModelBase
    {
        readonly MessageListener _messageListener;
        ISimpleDataSource _simpleDataSource;

        public MainWindowViewModel()
        {
            ConfigProvider config = GetConfigProvider();

            this.Messages = new ObservableCollection<MessageInfo>();

            _messageListener = new MessageListener(this.Messages, LogManager.GetLogger("root"));

            this.IncludeEpub = config.IncludeEpub;
            this.IncludeMobi = config.IncludeMobi;

            SetupDataSources(config);

            XmlConfigurator.Configure();
        }

        private void SetupDataSources(ConfigProvider config)
        {
            var dataSources = new ISimpleDataSourceInfo[]
            {
                new FileSystemDataSourceInfo(_messageListener) {Parameter = config.ImportFolderPath},
                new FileSystemDataSourceInfo(_messageListener) {Parameter = @"c:\tmp\"},
                new SqlDataSourceInfo {Parameter = "Server=localhost; Database=ebook; Trusted_Connection=SSPI"},
                new SqlLiteDataSourceInfo {Parameter = @"C:\Tree\ebook\sql\dev.db"},
            };

            this.SimpleDataSourceInfoList = new ObservableCollection<ISimpleDataSourceInfo>(dataSources);

            this.SelectedSimpleDataSourceInfo = dataSources[0];

            var sources = new IFullDataSourceInfo[]
            {
                new SqlLiteDataSourceInfo {Parameter = @"C:\Tree\ebook\sql\dev.db"},
                new SqlDataSourceInfo {Parameter = "Server=localhost; Database=ebook; Trusted_Connection=SSPI"},
            };

            this.FullDataSourceInfoList = new ObservableCollection<IFullDataSourceInfo>(sources);

            this.SelectedFullDataSourceInfo = sources[0];
        }

        ConfigProvider GetConfigProvider()
        {
            return new ConfigFile().GetConfigProvider();
        }

        async Task DoView()
        {
            if (this.SelectedSimpleDataSourceInfo == null)
                return;

            _simpleDataSource = this.SelectedSimpleDataSourceInfo.GetSimpleDataSource();

            _messageListener.Write("View: starting");
            this.IsBusy = true;

            IEnumerable<BookInfo> books = await _simpleDataSource.GetBooks(this.IncludeMobi, this.IncludeEpub);
            IEnumerable<MatchInfo> matches = books.Select(b => new MatchInfo(b));
            this.BookFileList = new ObservableCollection<MatchInfo>(matches);

            this.IsBusy = false;
            _messageListener.Write("View: loaded {0} books", this.BookFileList.Count);
        }

        async Task TryDoMatch()
        {
            try
            {
                await DoMatch();
            }
            catch (Exception ex)
            {
                ExceptionHandler.Handle(ex, "comparing");
            }
        }

        async Task DoMatch()
        {
            if (this.SelectedFullDataSourceInfo == null)
                return;

            _messageListener.Write("Compare: starting");

            this.IsBusy = true;

            var bookFinder = new QueryBookFinder(this.SelectedFullDataSourceInfo.GetFullDataSource());
            //var bookFinder = new BookFinder(this.SelectedFullDataSourceInfo.GetFullDataSource());

            var matcher = new Matcher(bookFinder, _messageListener);

            IEnumerable<MatchInfo> matched = await matcher.Match(this.BookFileList);

            //this.BookFileList = new ObservableCollection<MatchInfo>(matched);
            MergeMatchInfos(matched);

            this.IsBusy = false;

            _messageListener.Write("Compare: compared {0} books", this.BookFileList.Count);
        }

        void MergeMatchInfos(IEnumerable<MatchInfo> updated)
        {
            IEnumerable<MatchInfo> newlist = GetMergedList(this.BookFileList, updated);

            this.BookFileList = new ObservableCollection<MatchInfo>(newlist);
        }

        IEnumerable<MatchInfo> GetMergedList(IEnumerable<MatchInfo> original, IEnumerable<MatchInfo> updated)
        {
            var newlist = new List<MatchInfo>();

            var lookup = updated.ToDictionary(m => m.Book.Id);

            foreach (MatchInfo match in original)
            {
                if (lookup.ContainsKey(match.Book.Id))
                {
                    newlist.Add(lookup[match.Book.Id]);
                }
                else
                {
                    newlist.Add(match);
                }
            }

            return newlist;
        }

        async Task DoUpload()
        {
            if (this.BookFileList == null)
                return;

            if (this.SelectedFullDataSourceInfo == null)
                return;

            await UploadBooks();
        }

        async Task UploadBooks()
        {
            _messageListener.Write("Upload: starting");

            this.IsBusy = true;

            var uploader = new Uploader(this.SelectedFullDataSourceInfo.GetFullDataSource(), _simpleDataSource, _messageListener);
            uploader.DateAddedText = DateAddedText;

            var contents = await uploader.Upload(this.BookFileList);

            this.IsBusy = false;

            _messageListener.Write("Upload: uploaded {0} books", contents.Count());
        }

        void SelectedSimpleDataSourceInfoChanged()
        {
            this.BookFileList = new ObservableCollection<MatchInfo>();
        }

        async Task SelectedBookChanged()
        {
            if (this.SelectedBook == null)
            {
                this.SelectedBookContent = null;
                return;
            }

            this.SelectedBookContent = await _simpleDataSource.GetBookContent(this.SelectedBook.Book);
        }

        #region Properties
        bool _isBusy;
        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (value == _isBusy)
                    return;

                _isBusy = value;
                base.RaisePropertyChanged();
            }
        }

        string _dateAddedText;
        public string DateAddedText
        {
            get { return _dateAddedText; }
            set
            {
                if (value == _dateAddedText)
                    return;

                _dateAddedText = value;
                base.RaisePropertyChanged();
            }
        }

        MatchInfo _selectedBook;

        public MatchInfo SelectedBook
        {
            get { return _selectedBook; }
            set
            {
                if (value == _selectedBook)
                    return;
                _selectedBook = value;
                RaisePropertyChanged();

                Task.Run(SelectedBookChanged);
            }
        }

        BookFilesInfo _selectedBookContent;

        public BookFilesInfo SelectedBookContent
        {
            get { return _selectedBookContent; }
            set
            {
                if (value == _selectedBookContent)
                    return;
                _selectedBookContent = value;
                RaisePropertyChanged();
            }
        }

        ObservableCollection<IFullDataSourceInfo> _fullDataSourceInfoList;

        public ObservableCollection<IFullDataSourceInfo> FullDataSourceInfoList
        {
            get { return _fullDataSourceInfoList; }
            set
            {
                if (value == _fullDataSourceInfoList)
                    return;
                _fullDataSourceInfoList = value;
                RaisePropertyChanged();
            }
        }

        IFullDataSourceInfo _selectedFullDataSourceInfo;

        public IFullDataSourceInfo SelectedFullDataSourceInfo
        {
            get { return _selectedFullDataSourceInfo; }
            set
            {
                if (value == _selectedFullDataSourceInfo)
                    return;
                _selectedFullDataSourceInfo = value;
                RaisePropertyChanged();
            }
        }

        ObservableCollection<ISimpleDataSourceInfo> _simpleDataSourceInfoList;

        public ObservableCollection<ISimpleDataSourceInfo> SimpleDataSourceInfoList
        {
            get { return _simpleDataSourceInfoList; }
            set
            {
                if (value == _simpleDataSourceInfoList)
                    return;
                _simpleDataSourceInfoList = value;
                RaisePropertyChanged();
            }
        }

        ISimpleDataSourceInfo _selectedSimpleDataSourceInfo;

        public ISimpleDataSourceInfo SelectedSimpleDataSourceInfo
        {
            get { return _selectedSimpleDataSourceInfo; }
            set
            {
                if (value == _selectedSimpleDataSourceInfo)
                    return;
                _selectedSimpleDataSourceInfo = value;
                RaisePropertyChanged();
                SelectedSimpleDataSourceInfoChanged();
            }
        }

        ObservableCollection<MatchInfo> _bookFileList;

        public ObservableCollection<MatchInfo> BookFileList
        {
            get { return _bookFileList; }
            set
            {
                if (value == _bookFileList)
                    return;
                _bookFileList = value;
                RaisePropertyChanged();
            }
        }
        
        bool _includeEpub;
        public bool IncludeEpub
        {
            get { return _includeEpub; }
            set
            {
                if (value == _includeEpub)
                    return;

                _includeEpub = value;
                base.RaisePropertyChanged();
            }
        }

        bool _includeMobi;
        public bool IncludeMobi
        {
            get { return _includeMobi; }
            set
            {
                if (value == _includeMobi)
                    return;

                _includeMobi = value;
                base.RaisePropertyChanged();
            }
        }

        ObservableCollection<MessageInfo> _messages;

        public ObservableCollection<MessageInfo> Messages
        {
            get { return _messages; }
            set
            {
                if (value == _messages)
                    return;
                _messages = value;
                RaisePropertyChanged();
            }
        }
        #endregion

        #region Commands
        ICommand _viewCommand;

        public ICommand ViewCommand
        {
            get { return _viewCommand ?? (_viewCommand = new AsyncCommand1(this.DoView)); }
        }

        ICommand _matchCommand;

        public ICommand MatchCommand
        {
            get { return _matchCommand ?? (_matchCommand = new AsyncCommand1(TryDoMatch)); }
        }

        ICommand _uploadCommand;

        public ICommand UploadCommand
        {
            get { return _uploadCommand ?? (_uploadCommand = new AsyncCommand1(this.DoUpload)); }
        }
        #endregion
    }
}
