using beta.Models.Server;
using beta.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace beta.Infrastructure.Services.Interfaces
{
    public enum ComparisonMethod
    {
        STARTS_WITH = 0,
        CONTAINS = 1
    }
    public class PlayersService : ViewModel, IPlayersService
    {
        
        #region Properties
        
        #region Services

        private readonly ISessionService SessionService;
        private readonly IAvatarService AvatarService;

        #endregion

        #region Players

        public readonly ObservableCollection<PlayerInfoMessage> _Players = new();
        public ObservableCollection<PlayerInfoMessage> Players => _Players;

        private readonly Dictionary<int, int> PlayerUIDToId = new();
        private readonly Dictionary<string, int> PlayerLoginToId = new();

        #endregion

        #endregion

        #region Ctor

        public PlayersService(ISessionService sessionService, IAvatarService avatarService)
        {
            SessionService = sessionService;
            AvatarService = avatarService;

            sessionService.NewPlayer += OnNewPlayer;
        }

        #endregion

        private void OnNewPlayer(object sender, EventArgs<PlayerInfoMessage> e)
        {
            var player = e.Arg;
            if (PlayerUIDToId.TryGetValue(player.id, out var id))
            {
                Players[id].Update(player);
                Players[id].Updated = DateTime.Now;
                //var cachedPlayer = Players[id];

                // TODO: update stats manually

                //player.Updated = DateTime.UtcNow;
                //_Players[id] = player;
            }
            else
            {
                int count = _Players.Count;

                // Check for avatar
                if (player.avatar != null)
                {
                    // TODO FIX ME Thread access error. BitmapImage should be created in UI thread
                    App.Current.Dispatcher.Invoke(() => player.Avatar = AvatarService.GetAvatar(player.avatar.url),
                    System.Windows.Threading.DispatcherPriority.Background);
                }

                _Players.Add(player);
                PlayerLoginToId.Add(player.login.ToLower(), count);
                PlayerUIDToId.Add(player.id, count);
            }
        }

        public PlayerInfoMessage GetPlayer(string login)
        {
            login = login.ToLower();
            if (login.Length <= 0 || login.Trim().Length <= 0) return null;

            if (PlayerLoginToId.TryGetValue(login, out var id))
            {
                return Players[id];
            }


            return null;
        }

        public PlayerInfoMessage GetPlayer(int uid)
        {
            if (PlayerUIDToId.TryGetValue(uid, out var id))
            {
                return Players[id];
            }

            return null;
        }


        public IEnumerable<PlayerInfoMessage> GetPlayers(string filter = null, ComparisonMethod method = ComparisonMethod.STARTS_WITH)
        {
            var enumerator = _Players.GetEnumerator();

            if (string.IsNullOrWhiteSpace(filter))
                while (enumerator.MoveNext())
                    yield return enumerator.Current;
            else
                while (enumerator.MoveNext())
                    if (method == ComparisonMethod.STARTS_WITH)
                        if (enumerator.Current.login.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                            yield return enumerator.Current;
                    else if (enumerator.Current.login.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        yield return enumerator.Current;
                
        }
    }
}
