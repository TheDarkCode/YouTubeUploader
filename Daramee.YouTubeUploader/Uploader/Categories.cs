﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.YouTube.v3.Data;

namespace Daramee.YouTubeUploader.Uploader
{
	public class VideoCategory
	{
		public string Name { get; set; }
		public string Id { get; set; }
	}

	public class Categories
	{
		public static IReadOnlyList<VideoCategory> DetectedCategories { get; private set; }
			= new ObservableCollection<VideoCategory> () { new VideoCategory () { Name = "없음", Id = null } };

		public async Task Refresh ( YouTubeSession session )
		{
			while ( DetectedCategories.Count > 1 )
				( DetectedCategories as IList<VideoCategory> ).RemoveAt ( 1 );

			var request = session.YouTubeService.VideoCategories.List ( "snippet" );
			request.RegionCode = RegionInfo.CurrentRegion.TwoLetterISORegionName;
			request.Hl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

			VideoCategoryListResponse result = null;
			int retry = 0;
			do
			{
				result = await request.ExecuteAsync ();
				++retry;
				if ( retry == 30 )
					return;
			}
			while ( result == null || result.Items == null );

			foreach ( var i in result.Items )
			{
				if ( i.Snippet.Assignable == false )
					continue;
				VideoCategory item = new VideoCategory ();
				item.Name = i.Snippet.Title;
				item.Id = i.Id;
				( DetectedCategories as IList<VideoCategory> ).Add ( item );
			}
		}
	}
}
