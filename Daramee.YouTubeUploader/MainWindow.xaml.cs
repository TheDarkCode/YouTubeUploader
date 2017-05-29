﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Daramee.YouTubeUploader.Uploader;
using Microsoft.Win32;

namespace Daramee.YouTubeUploader
{
	/// <summary>
	/// MainWindow.xaml에 대한 상호 작용 논리
	/// </summary>
	public sealed partial class MainWindow : Window, IDisposable
	{
		public static MainWindow SharedWindow { get; private set; }

		YouTubeSession youtubeSession = new YouTubeSession ( Environment.CurrentDirectory );

		public bool HaltWhenAllCompleted { get; set; } = false;

		public MainWindow ()
		{
			SharedWindow = this;

			RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

			InitializeComponent ();
			TaskbarItemInfo = new TaskbarItemInfo ();

			uploadQueueListBox.ItemsSource = new ObservableCollection<UploadQueueItem> ();
		}

		private void AddItem ( string filename )
		{
			if ( !youtubeSession.IsAlreadyAuthorized )
				return;

			bool alreadyAdded = false;
			Uri filenameUri = new Uri ( filename );
			foreach ( var item in uploadQueueListBox.ItemsSource as IList<UploadQueueItem> )
			{
				if ( item.FileName.AbsolutePath == filenameUri.AbsolutePath )
				{
					alreadyAdded = true;
					break;
				}
			}

			if ( alreadyAdded )
				return;

			var queueItem = new UploadQueueItem ( youtubeSession, filename ) { PrivacyStatus = ( PrivacyStatus ) comboBoxDefaultPrivacyStatus.SelectedIndex };
			queueItem.Completed += ( sender, e ) =>
			{
				if ( !HaltWhenAllCompleted )
					return;

				var list = uploadQueueListBox.ItemsSource as IList<UploadQueueItem>;
				bool incompleted = false;
				foreach ( var i in list )
				{
					if ( i.UploadingStatus == UploadingStatus.UploadCompleted )
						continue;
					incompleted = true;
					break;
				}

				if ( !incompleted )
				{
					var psi = new ProcessStartInfo ( "shutdown", "/s /t 10" );
					psi.CreateNoWindow = true;
					psi.UseShellExecute = false;
					Process.Start ( psi );
				}
			};
			queueItem.Failed += async ( sender, e ) =>
			{
				if ( !HaltWhenAllCompleted )
					return;

				Thread.Sleep ( 1000 * 60 * 15 );

				if ( !HaltWhenAllCompleted )
					return;

				await ( sender as UploadQueueItem ).UploadStart ();
			};
			queueItem.Uploading += ( sender, e ) =>
			{
				Dispatcher.BeginInvoke ( new Action ( () =>
				{
					double totalProgress = 0;
					bool thereIsFailedItem = false;
					int totalCount = 0;

					foreach ( var item in uploadQueueListBox.ItemsSource as ObservableCollection<UploadQueueItem> )
					{
						if ( item.UploadingStatus == UploadingStatus.UploadFailed )
							thereIsFailedItem = true;
						else if ( item.UploadingStatus == UploadingStatus.Queued )
							continue;
						totalProgress += item.Progress;
						++totalCount;
					}

					TaskbarItemInfo.ProgressState = thereIsFailedItem ? TaskbarItemProgressState.Paused : TaskbarItemProgressState.Normal;
					TaskbarItemInfo.ProgressValue = totalProgress / totalCount;
				} ) );
			};
			( uploadQueueListBox.ItemsSource as IList<UploadQueueItem> ).Add ( queueItem );
		}

		private void Window_Loaded ( object sender, RoutedEventArgs e )
		{
			if ( youtubeSession.IsAlreadyAuthorized )
				ButtonConnect_Click ( sender, e );
		}

		private void Window_Closing ( object sender, System.ComponentModel.CancelEventArgs e )
		{
			var list = uploadQueueListBox.ItemsSource as IList<UploadQueueItem>;
			bool incompleted = false;
			foreach ( var i in list )
			{
				if ( i.UploadingStatus == UploadingStatus.UploadCompleted )
					continue;
				incompleted = true;
				break;
			}

			if ( incompleted )
			{
				var result = MessageBox.Show ( "아직 업로드가 끝나지 않았습니다.\n그대로 종료하시겠습니까?", "안내", MessageBoxButton.YesNo, MessageBoxImage.Asterisk );
				if ( result == MessageBoxResult.No )
				{
					e.Cancel = true;
					return;
				}
			}
		}

		public void Dispose ()
		{
			youtubeSession.Dispose ();
		}

		private void ButtonOpen_Click ( object sender, RoutedEventArgs e )
		{
			OpenFileDialog ofd = new OpenFileDialog ();
			ofd.Filter = "All Available Files(*.mp4;*.mkv;*.webm;*.avi;*.mov;*.flv;*.wmv;*.3gp)|*.mp4;*.mkv;*.webm;*.avi;*.mov;*.flv;*.wmv;*.3gp";
			ofd.Multiselect = true;
			if ( ofd.ShowDialog () == false )
				return;

			foreach ( var filename in ofd.FileNames )
			{
				AddItem ( filename );
			}
		}

		private async void ButtonConnect_Click ( object sender, RoutedEventArgs e )
		{
			if ( await youtubeSession.Authorization () )
			{
				buttonOpen.IsEnabled = true;
				buttonConnect.IsEnabled = false;
				buttonDisconnect.IsEnabled = true;
				comboBoxDefaultPrivacyStatus.IsEnabled = true;
				buttonAllUpload.IsEnabled = true;
				haltWhenCompleteCheckBox.IsEnabled = true;
			}
		}

		private void ButtonDisconnect_Click ( object sender, RoutedEventArgs e )
		{
			youtubeSession.Unauthorization ();
			buttonOpen.IsEnabled = false;
			buttonConnect.IsEnabled = true;
			buttonDisconnect.IsEnabled = false;
			comboBoxDefaultPrivacyStatus.IsEnabled = false;
			buttonAllUpload.IsEnabled = false;
			haltWhenCompleteCheckBox.IsEnabled = false;
		}

		private void ButtonPlayItem_Click ( object sender, RoutedEventArgs e )
		{
			( ( ( ( sender as Button ).Parent as StackPanel ).Parent as Grid ).Children [ 1 ] as MediaElement ).Play ();
		}

		private void ButtonStopItem_Click ( object sender, RoutedEventArgs e )
		{
			var mediaElement = ( ( ( ( sender as Button ).Parent as StackPanel ).Parent as Grid ).Children [ 1 ] as MediaElement );
			mediaElement.Stop ();
			mediaElement.Close ();
		}

		private void ButtonRemoveItem_Click ( object sender, RoutedEventArgs e )
		{
			var item = ( sender as Button ).DataContext as UploadQueueItem;
			if ( item.UploadingStatus == UploadingStatus.UploadFailed )
			{
				if ( MessageBox.Show ( "이 항목을 지우면 업로드를 이어서 하지 못하게 됩니다.\n정말로 이 항목을 목록에서 제거하시겠습니까?",
					"안내", MessageBoxButton.YesNo, MessageBoxImage.Asterisk ) == MessageBoxResult.No )
					return;
			}

			( uploadQueueListBox.ItemsSource as IList<UploadQueueItem> ).Remove ( item );
		}

		private async void ButtonUpload_Click ( object sender, RoutedEventArgs e )
		{
			var uploadQueueItem = ( ( sender as Button ).DataContext as UploadQueueItem );
			await UploadItem ( uploadQueueItem );
		}

		private void HyperlinkBrowse_Click ( object sender, RoutedEventArgs e )
		{
			OpenFileDialog ofd = new OpenFileDialog ();
			ofd.Filter = "All Available Files(*.jpg;*.png)|*.jpg;*.png";
			if ( ofd.ShowDialog () == false )
				return;

			BitmapImage bitmapSource = new BitmapImage ();
			bitmapSource.BeginInit ();
			bitmapSource.UriSource = new Uri ( ofd.FileName );
			bitmapSource.EndInit ();
			bitmapSource.Freeze ();

			if ( Math.Abs ( ( bitmapSource.Width / ( double ) bitmapSource.Height ) - ( 16 / 9.0 ) ) >= float.Epsilon )
			{
				MessageBox.Show ( "이미지 크기가 16:9 비율이어야 합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Exclamation );
				return;
			}

			( ( sender as Hyperlink ).DataContext as UploadQueueItem ).Thumbnail = bitmapSource;
		}

		private void HyperlinkClipboard_Click ( object sender, RoutedEventArgs e )
		{
			if ( !Clipboard.ContainsImage () )
				return;

			var bitmapSource = Clipboard.GetImage ();
			bitmapSource.Freeze ();

			if ( Math.Abs ( ( bitmapSource.Width / ( double ) bitmapSource.Height ) - ( 16 / 9.0 ) ) >= float.Epsilon )
			{
				MessageBox.Show ( "이미지 크기가 16:9 비율이어야 합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Exclamation );
				return;
			}

			( ( sender as Hyperlink ).DataContext as UploadQueueItem ).Thumbnail = bitmapSource;
		}

		private void HyperlinkEditTags_Click ( object sender, RoutedEventArgs e )
		{
			var item = ( ( sender as Hyperlink ).DataContext as UploadQueueItem );
			TagEditorWindow window = new TagEditorWindow ( item.Tags as ObservableCollection<string> );
			window.ShowDialog ();
		}

		private void uploadQueueListBox_Drop ( object sender, DragEventArgs e )
		{
			if ( !e.Data.GetDataPresent ( DataFormats.FileDrop ) )
				return;

			foreach ( var filename in e.Data.GetData ( DataFormats.FileDrop ) as string [] )
			{
				string extension = System.IO.Path.GetExtension ( filename ).ToLower ();
				if ( !( extension == ".mp4" || extension == ".mkv" || extension == ".webm" || extension == ".avi" || extension == ".mov" ||
					extension == ".flv" || extension == ".wmv" || extension == ".3gp" ) )
					continue;

				AddItem ( filename );
			}
		}

		private void uploadQueueListBox_DragEnter ( object sender, DragEventArgs e )
		{
			if ( e.Data.GetDataPresent ( DataFormats.FileDrop ) )
				e.Effects = DragDropEffects.None;
		}

		private void haltWhenCompleteCheckBox_Checked ( object sender, RoutedEventArgs e )
		{
			MessageBox.Show ( @"모든 업로드가 성공적으로 완료되면
10초 후 자동으로 컴퓨터를 종료하는 기능입니다.

이 기능이 켜져있으면서 업로드에 실패한 경우
15분 후 실패한 업로드를 재업로드 시도합니다.

추가 안내 없이 종료를 시작하므로 주의하십시오.
이 기능은 shutdown 명령어를 사용합니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information );
		}

		private void ButtonAllUpload_Click ( object sender, RoutedEventArgs e )
		{
			foreach ( var item in uploadQueueListBox.ItemsSource as ObservableCollection<UploadQueueItem> )
			{
				if ( item.UploadingStatus == UploadingStatus.Queued || item.UploadingStatus == UploadingStatus.UploadFailed )
					ThreadPool.QueueUserWorkItem ( async ( i )=> { await UploadItem ( i as UploadQueueItem ); }, item );
			}
		}

		private async Task UploadItem ( UploadQueueItem uploadQueueItem )
		{
			if ( uploadQueueItem.Title.Trim ().Length == 0 )
			{
				MessageBox.Show ( "영상 제목은 반드시 채워져야 합니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error );
				return;
			}

			switch ( await uploadQueueItem.UploadStart () )
			{
				case UploadResult.AlreadyUploading:
					MessageBox.Show ( "이미 업로드가 시작되었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Error );
					break;
				case UploadResult.CannotAccesToFile:
					MessageBox.Show ( "영상 파일에 접근할 수 없었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Error );
					break;
				case UploadResult.FailedUploadRequest:
					MessageBox.Show ( "업로드 요청을 시작할 수 없었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Error );
					break;
				case UploadResult.CannotStartUpload:
					MessageBox.Show ( "업로드 작업을 시작할 수 없었습니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Error );
					break;
			}
		}
	}
}
