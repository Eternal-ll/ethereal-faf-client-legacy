﻿using beta.Models.API;
using beta.Models.Server.Enums;
using LiveCharts;
using LiveCharts.Defaults;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace beta.ViewModels
{
    /// <summary>
    /// 
    /// </summary>
    internal class ApiRatingsViewModel : ApiPlayerViewModel
    {
        public RatingType[] RatingTypes { get; private set; }

        private readonly Dictionary<RatingType, ApiGamePlayerStats[]> Data = new();
        private static string ConvertTicksToDateTimeString(double value)
          => new DateTime((long)value).ToString();

        public ChartValues<ObservablePoint> SeriesValues { get; private set; }
        public Func<double, string> LabelFormatter => ConvertTicksToDateTimeString;


        public ApiRatingsViewModel(int playerId, params RatingType[] ratingTypes) : base(playerId)
        {
            if (ratingTypes.Length == 0) return;
            RatingTypes = ratingTypes;
            for (int i = 0; i < ratingTypes.Length; i++)
            {
                Data.Add(ratingTypes[i], null);
            }
            SelectedRatingType = ratingTypes[0];
        }

        #region SelectedRatingType
        private RatingType _SelectedRatingType;
        public RatingType SelectedRatingType
        {
            get => _SelectedRatingType;
            set
            {
                if (Set(ref _SelectedRatingType, value))
                {
                    RunRequest();
                }
            }
        }
        #endregion

        public ApiGamePlayerStats[] SelectedRatingData => Data[SelectedRatingType];

        protected override async Task RequestTask()
        {
            if (IsRefreshing)
            {
                Data[SelectedRatingType] = null;
            }
            var url = $"https://api.faforever.com/data/gamePlayerStats?filter=(player.id=={PlayerId};ratingChanges.leaderboard.id=={(int)SelectedRatingType})&fields[gamePlayerStats]=afterDeviation,afterMean,beforeDeviation,beforeMean,scoreTime&page[totals]=yes&page[size]=500";
            var result = await ApiRequest<ApiUniversalResultWithMeta<ApiGamePlayerStats[]>>.Request(url);
            List<ApiGamePlayerStats> data = new();
            var pages = result.Meta.Page.AvaiablePagesCount;
            var index = 0;
            int last = 0;
            DateTime lastDate = DateTime.Now;
            for (int i = 1; i <= pages; i++)
            {
                result = await ApiRequest<ApiUniversalResultWithMeta<ApiGamePlayerStats[]>>.Request(url + $"&page[number]=" + i);
                for (int j = 0; j < result.Data.Length; j++)
                {
                    var difference = result.Data[j].RatingAfter - last;
                    last = result.Data[j].RatingAfter;
                    index++;
                    data.Add(result.Data[j]);
                }
            }
            Data[SelectedRatingType] = data.ToArray();
            OnPropertyChanged(nameof(SelectedRatingData));
        }
    }
}
