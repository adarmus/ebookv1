﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ebook.Async;
using ebook.core.DataTypes;
using ebook.core.Email;
using ebook.core.Files;
using ebook.core.Logging;
using ebook.core.Logic;
using ebook.core.Mobi.Pdb;
using ebook.core.Repo;
using ebook.core.Repo.File;
using ebook.core.Repo.SqlLite;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using log4net;
using log4net.Config;
using Microsoft.Win32;

namespace ebook
{
    internal class MainWindowViewModel : ViewModelBase
    {
        readonly MessageListener _messageListener;
        readonly ExceptionHandler _exceptionHandler;

        ISimpleDataSource _simpleDataSource;

        public MainWindowViewModel()
        {
            ConfigProvider config = GetConfigProvider();

            this.Messages = new ObservableCollection<MessageInfo>();

            _messageListener = new MessageListener(this.Messages);
            _exceptionHandler = new ExceptionHandler(_messageListener);

            this.IncludeEpub = config.IncludeEpub;
            this.IncludeMobi = config.IncludeMobi;

            SetupDataSources(config);

            //TestDebug();

            XmlConfigurator.Configure();
        }
        
        void TestDebug()
        {
            var db = new SqlLiteDataSource(@"C:\Tree\ebook\sql\dev.db");
            var b = db.GetBookContent(new BookInfo {Id = "783a0438-4095-449f-8ea7-195cbf07cb65" }).Result;
            Console.WriteLine("hi");
        }

        private void SetupDataSources(ConfigProvider config)
        {
            var dataSources = new ISimpleDataSourceInfo[]
            {
                new FileSystemDataSourceInfo(_exceptionHandler) {Parameter = config.ImportFolderPath},
                new SqlDataSourceInfo {Parameter = config.SqlConnectionString},
                new SqlLiteRefDataSourceInfo {Parameter = config.SqliteFilepath},
                new SqlLiteDataSourceInfo {Parameter = config.SqliteFilepath},
            };

            this.SimpleDataSourceInfoList = new ObservableCollection<ISimpleDataSourceInfo>(dataSources);

            this.SelectedSimpleDataSourceInfo = dataSources[0];

            var sources = new IFullDataSourceInfo[]
            {
                new SqlLiteRefDataSourceInfo {Parameter = config.SqliteFilepath},
                new SqlLiteDataSourceInfo {Parameter = config.SqliteFilepath},
                new SqlDataSourceInfo {Parameter = config.SqlConnectionString},
            };

            this.FullDataSourceInfoList = new ObservableCollection<IFullDataSourceInfo>(sources);

            this.SelectedFullDataSourceInfo = sources[0];
        }

        ConfigProvider GetConfigProvider()
        {
            return new ConfigFile().GetConfigProvider();
        }

        async Task TryDoView()
        {
            if (this.SelectedFullDataSourceInfo == null)
                return;

            this.IsBusy = true;

            try
            {
                await DoView();
            }
            catch (Exception ex)
            {
                _exceptionHandler.WriteError(ex, "viewing");
            }

            this.IsBusy = false;
        }

        async Task DoView()
        {
            this.BookFileList = new ObservableCollection<MatchInfo>();

            _simpleDataSource = this.SelectedSimpleDataSourceInfo.GetSimpleDataSource();
            SetDateAddedProvider(_simpleDataSource);

            _messageListener.Write("View: starting");

            IEnumerable<BookInfo> books = await _simpleDataSource.GetBooks(this.IncludeMobi, this.IncludeEpub);
            IEnumerable<MatchInfo> matches = books.Select(b => new MatchInfo(b) { IsSelected = true });

            SetBookFileList(matches);

            _messageListener.Write("View: loaded {0} books", this.BookFileList.Count);
        }

        void SetDateAddedProvider(ISimpleDataSource dataSource)
        {
            var files = dataSource as FileBasedSimpleDataSource;

            if (files == null)
                return;

            files.DateAddedProvider = new DateAddedProvider(this.DateAddedText);
        }

        async Task TryDoMatch()
        {
            if (this.SelectedFullDataSourceInfo == null)
                return;

            this.IsBusy = true;

            try
            {
                await DoMatch();
            }
            catch (Exception ex)
            {
                _exceptionHandler.WriteError(ex, "comparing");
            }

            this.IsBusy = false;
        }

        async Task DoMatch()
        {
            _messageListener.Write("Compare: starting");

            var bookFinder = new QueryBookFinder(this.SelectedFullDataSourceInfo.GetFullDataSource());

            var matcher = new Matcher(bookFinder, _exceptionHandler);

            IEnumerable<MatchInfo> matched = await matcher.Match(this.BookFileList);

            MergeMatchInfos(matched);

            _messageListener.Write("Compare: compared {0} books", matched.Count());
        }

        void MergeMatchInfos(IEnumerable<MatchInfo> updated)
        {
            IEnumerable<MatchInfo> newlist = GetMergedList(this.BookFileList, updated);

            SetBookFileList(newlist);
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

        async Task TryDoUpload()
        {
            if (this.BookFileList == null)
                return;

            if (this.SelectedFullDataSourceInfo == null)
                return;

            this.IsBusy = true;

            try
            {
                await DoUpload();
            }
            catch (Exception ex)
            {
                _exceptionHandler.WriteError(ex, "uploading");
            }

            this.IsBusy = false;
        }

        async Task DoUpload()
        {
            _messageListener.Write("Upload: starting");

            this.IsBusy = true;

            var uploader = new Uploader(this.SelectedFullDataSourceInfo.GetFullDataSource(), _simpleDataSource, _exceptionHandler);
            uploader.DateAddedText = DateAddedText;

            UploadResults results = await uploader.Upload(this.BookFileList);

            this.IsBusy = false;

            _messageListener.Write("Upload: uploaded {0} books ({1} errors)", results.Uploaded, results.Errors);
        }

        void SelectedSimpleDataSourceInfoChanged()
        {
            SetBookFileList(new MatchInfo[] {});
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

        async Task DoExport()
        {
            if (this.SelectedBook == null)
                return ;

            string filepath = GetExportFilePath();

            if (filepath == null)
                return;

            await ExportEpubToFile(this.SelectedBook.Book, filepath);
        }

        async Task ExportEpubToFile(BookInfo book, string filepath)
        {
            var content = await _simpleDataSource.GetBookContent(book);

            var epub = content.Files.FirstOrDefault(f => f.FileType == BookExtensions.EPUB);

            if (epub == null)
                return;

            var file = new FileReader();
            await file.WriteFileAsync(filepath, epub.Content);
        }

        async Task DoEmail()
        {
            if (this.SelectedBook == null)
                return;

            await EmailMobi(this.SelectedBook.Book);
        }

        async Task EmailMobi(BookInfo book)
        {
            var content = await _simpleDataSource.GetBookContent(book);

            var mobi = content.Files.FirstOrDefault(f => f.FileType == BookExtensions.MOBI);

            if (mobi == null)
                return;

            var config = EmailServiceConfigurationSection.GetConfigSection();

            var email = new EmailService(new TraceLogger(""), config.GetConfiguration());

            email.SendHtmlEmail(book.Title, "", mobi.Content, book.Title, new[] {"adam@blairfamily.co.uk"});
        }

        string GetExportFilePath()
        {
            var saveDialog = new SaveFileDialog();
            saveDialog.OverwritePrompt = true;
            saveDialog.Title = "Export to epub";
            saveDialog.AddExtension = true;
            saveDialog.DefaultExt = ".epub";
            bool? result = saveDialog.ShowDialog();

            if (!result.Value)
                return null;

            return saveDialog.FileName;
        }

        void DoSelectAll()
        {
            SetIsSelected(true);
        }

        void DoDeSelectAll()
        {
            SetIsSelected(false);
        }

        void SetIsSelected(bool state)
        {
            foreach (var match in this.BookFileList)
            {
                match.IsSelected = state;
            }

            SetBookFileList(this.BookFileList);
        }

        void SetBookFileList(IEnumerable<MatchInfo> matches)
        {
            this.BookFileList = new ObservableCollection<MatchInfo>(matches);
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
        ICommand _emailCommand;

        public ICommand EmailCommand
        {
            get { return _emailCommand ?? (_emailCommand = new AsyncCommand1(this.DoEmail)); }
        }

        ICommand _exportCommand;

        public ICommand ExportCommand
        {
            get { return _exportCommand ?? (_exportCommand = new AsyncCommand1(this.DoExport)); }
        }

        ICommand _viewCommand;

        public ICommand ViewCommand
        {
            get { return _viewCommand ?? (_viewCommand = new AsyncCommand1(this.TryDoView)); }
        }

        ICommand _matchCommand;

        public ICommand MatchCommand
        {
            get { return _matchCommand ?? (_matchCommand = new AsyncCommand1(TryDoMatch)); }
        }

        ICommand _uploadCommand;

        public ICommand UploadCommand
        {
            get { return _uploadCommand ?? (_uploadCommand = new AsyncCommand1(this.TryDoUpload)); }
        }

        ICommand _selectAllCommand;

        public ICommand SelectAllCommand
        {
            get { return _selectAllCommand ?? (_selectAllCommand = new RelayCommand(this.DoSelectAll)); }
        }

        ICommand _deselectAllCommand;

        public ICommand DeselectAllCommand
        {
            get { return _deselectAllCommand ?? (_deselectAllCommand = new RelayCommand(this.DoDeSelectAll)); }
        }
        #endregion
    }
}
