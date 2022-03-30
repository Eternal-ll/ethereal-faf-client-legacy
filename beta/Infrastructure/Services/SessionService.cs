﻿using beta.Infrastructure.Extensions;
using beta.Infrastructure.Services.Interfaces;
using beta.Models;
using beta.Models.Server;
using beta.Models.Server.Enums;
using beta.Properties;
#if DEBUG
using Microsoft.Extensions.Logging;
#endif
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using beta.Models.Debugger;
using beta.Models.Enums;
using beta.Infrastructure.Utils;
using beta.Models.Server.Base;

namespace beta.Infrastructure.Services
{
    public class SessionService : ISessionService
    {
        #region Events
        public event EventHandler<EventArgs<bool>> Authorized;
        public event EventHandler<EventArgs<PlayerInfoMessage>> NewPlayerReceived;
        public event EventHandler<EventArgs<GameInfoMessage>> NewGameReceived;
        public event EventHandler<EventArgs<SocialMessage>> SocialDataReceived;
        public event EventHandler<EventArgs<WelcomeMessage>> WelcomeDataReceived;
        public event EventHandler<EventArgs<NoticeMessage>> NotificationReceived;
        //public event EventHandler<EventArgs<QueueData>> QueueDataReceived;
        public event EventHandler<EventArgs<MatchMakerData>> MatchMakerDataReceived;
        public event EventHandler<EventArgs<GameLaunchData>> GameLaunchDataReceived;
        #endregion

        #region Properties

        private ManagedTcpClient Client;

        private readonly IOAuthService OAuthService;
        private readonly IIrcService IrcService;

        private readonly Dictionary<ServerCommand, Action<string>> Operations = new();
        private bool WaitingPong = false;
        private Stopwatch Stopwatch = new();
        private Stopwatch TimeOutWatcher = new();

#if DEBUG
        private readonly ILogger Logger;
#endif

        #endregion

        #region CTOR
        public SessionService(IOAuthService oAuthService, IIrcService ircService
#if DEBUG
            , ILogger<SessionService> logger
#endif
            )
        {
            OAuthService = oAuthService;
            IrcService = ircService;
#if DEBUG
            Logger = logger;
#endif
            OAuthService.StateChanged += OnAuthResult;

            #region Response actions for server
            Operations.Add(ServerCommand.notice, OnNoticeData);

            Operations.Add(ServerCommand.irc_password, OnIrcPassowrdData);
            Operations.Add(ServerCommand.welcome, OnWelcomeData);
            Operations.Add(ServerCommand.social, OnSocialData);

            Operations.Add(ServerCommand.player_info, OnPlayerData);
            Operations.Add(ServerCommand.game_info, OnGameData);
            Operations.Add(ServerCommand.matchmaker_info, OnMatchmakerData);

            Operations.Add(ServerCommand.ping, OnPing);
            Operations.Add(ServerCommand.pong, OnPong);

            Operations.Add(ServerCommand.invalid, OnInvalidData);
            #endregion

            //new Thread(() =>
            //{
            //    Stopwatch.Start();
            //    while (true)
            //    {
            //        if (TimeOutWatcher.Elapsed.TotalSeconds > 180)
            //            Ping();
            //        Thread.Sleep(6000);
            //    }
            //}).Start();
        }
        #endregion
        public void Connect()
        {
            Client = new(threadName: "TCP Lobby Client", port: 8002);
            Client.DataReceived += OnDataReceived;
        }
        public string GenerateUID(string session)
        {
            Logger.LogInformation($"Generating UID for session: {session}");
            
            if (string.IsNullOrWhiteSpace(session))
            {
                Logger.LogWarning("Passed session value is empty");
                return null;
            }

            string result = null;

            Logger.LogInformation("Getting path to faf-uid.exe");

            var file = Tools.GetFafUidFileInfo();
            Logger.LogInformation($"Got the path: {file}");
            Process process = new()
            {
                StartInfo = new()
                {
                    FileName = file.FullName,
                    Arguments = session,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                }
            };
            process.Start();

            Logger.LogInformation("Starting process of faf-uid.exe");
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                Logger.LogInformation($"Output line: {line}");
                result += line;
            }
            process.Close();
            Logger.LogInformation($"faf-uid.exe process is closed");
            return result;
        }
        public void Authorize()
        {
            Logger.LogInformation($"Starting authorization process to lobby server");

            // TODO Fix
            if (Client is null)
            {
                Client = new(port: 8002)
                {
                    ThreadName = "TCP Lobby Server"
                };
                Client.DataReceived += OnDataReceived;
                // TODO Requires some better logic maybe
                ManagedTcpClientState state = ManagedTcpClientState.Disconnected;
                Client.StateChanged += (s, e) =>
                {
                    state = e;
                    if (e == ManagedTcpClientState.CantConnect || e == ManagedTcpClientState.TimedOut)
                    {
                        // TODO Raise events
                        OnAuthorization(false);
                        return;
                    }
                };
                while (state != ManagedTcpClientState.Connected)
                {
                    Thread.Sleep(10);
                }
            }
            else
            {
                if (Client.TcpClient is null)
                {
                    Client = null;
                    Authorize();
                }
            }

            string session = GetSession();
            string accessToken = Settings.Default.access_token;
            string generatedUID = GenerateUID(session);


            string authJson = ServerCommands.PassAuthentication(accessToken, generatedUID, session);

            Logger.LogInformation($"Sending data for authentication to lobby-server...");

            Client.Write(authJson);
        }
        public string GetSession()
        {
            /*WRITE
            {
                "command": "ask_session",
                "version": "0.20.1+12-g2d1fa7ef.git",
                "user_agent": "faf-client"
            }*/

            // just a joke
            var response = Client.WriteLineAndGetReply(new byte[] {123, 34, 99, 111, 109, 109, 97, 110, 100, 34, 58, 34, 97, 115, 107, 95, 115, 101, 115, 115, 105, 111,
                110, 34, 44, 34, 118, 101, 114, 115, 105, 111, 110, 34, 58, 34, 48, 46, 50, 48, 46, 49, 92, 117, 48, 48,
                50, 66, 49, 50, 45, 103, 50, 100, 49, 102, 97, 55, 101, 102, 46, 103, 105, 116, 34, 44, 34, 117, 115,
                101, 114, 95, 97, 103, 101, 110, 116, 34, 58, 34, 102, 97, 102, 45, 99, 108, 105, 101, 110, 116, 34,
                125, 10}, ServerCommand.session, new(0, 0, 10));

            return response.GetRequiredJsonRowValue(2);
        }
        public void Send(string command)
        {
            Logger.LogInformation($"Sent to lobby-server:\n{command}");
            AppDebugger.LOGLobby($"Sent to lobby-server:\n {command.ToJsonFormat()}");
            Client.Write(command);
        }

        public void Ping()
        {
            WaitingPong = true;
            Stopwatch.Start();
            Client.Write(ServerCommands.Ping);
        }

        private void OnAuthResult(object sender, OAuthEventArgs e)
        {
            if (e.State == OAuthState.AUTHORIZED)
                Authorize();
        }

#if DEBUG
        private readonly List<ServerCommand> AllowedToDebugCommands = new()
        {
            //ServerCommand.notice,
            //ServerCommand.session,
        };
#endif

        private void OnDataReceived(object sender, string json)
        {
            //TimeOutWatcher.Restart();
            var commandText = json.GetRequiredJsonRowValue();
            if (Enum.TryParse<ServerCommand>(commandText, out var command))
            {
                if (Operations.TryGetValue(command, out var response))
                {
                    response.Invoke(json);

                    //AppDebugger.LOGLobby(json.ToJsonFormat());
                }

                else AppDebugger.LOGLobby($"-------------- WARNING! NO RESPONSE FOR COMMAND: {command} ----------------\n" + json.ToJsonFormat());

            }
            //else App.DebugWindow.LOGLobby($"-------------- WARNING! UNKNOWN COMMAND: {commandText} ----------------\n" + json.ToJsonFormat());
        }

        #region Events invokers
        protected virtual void OnAuthorization(EventArgs<bool> e) => Authorized?.Invoke(this, e);
        protected virtual void OnNewPlayer(EventArgs<PlayerInfoMessage> e) => NewPlayerReceived?.Invoke(this, e);
        protected virtual void OnNewGame(EventArgs<GameInfoMessage> e) => NewGameReceived?.Invoke(this, e);
        protected virtual void OnSocialInfo(EventArgs<SocialMessage> e) => SocialDataReceived?.Invoke(this, e);
        protected virtual void OnWelcomeInfo(EventArgs<WelcomeMessage> e) => WelcomeDataReceived?.Invoke(this, e);
        protected virtual void OnNotificationReceived(EventArgs<NoticeMessage> e) => NotificationReceived?.Invoke(this, e);
        protected virtual void OnMatchMakerDataReceived(EventArgs<MatchMakerData> e) => MatchMakerDataReceived?.Invoke(this, e);
        protected virtual void OnGameLaunchDataReceived(EventArgs<GameLaunchData> e) => GameLaunchDataReceived?.Invoke(this, e);
        #endregion

        #region Server response actions
        private void OnNoticeData(string json) => OnNotificationReceived(JsonSerializer.Deserialize<NoticeMessage>(json));
        private void OnWelcomeData(string json)
        {
            var welcomeMessage = JsonSerializer.Deserialize<WelcomeMessage>(json);
            Settings.Default.PlayerId = welcomeMessage.id;
            Settings.Default.PlayerNick = welcomeMessage.login;
            OnAuthorization(true);
            OnWelcomeInfo(welcomeMessage);
        }

        private void OnIrcPassowrdData(string json)
        {
            string password = json.GetRequiredJsonRowValue(2);
            Settings.Default.irc_password = password;

            if (Settings.Default.ConnectIRC)
            {
                //IrcService.Authorize(Settings.Default.PlayerNick, Settings.Default.irc_password);
            }
        }
        private void OnSocialData(string json)
        {
            // Do i really need to invoke Event? 
            OnSocialInfo(JsonSerializer.Deserialize<SocialMessage>(json));
        }
        private void OnInvalidData(string json = null)
        {
            // TODO FIX ME???? ERROR UP?
            Settings.Default.access_token = null;
            OAuthService.AuthAsync();
        }

        private void OnPlayerData(string json)
        {
            var playerInfoMessage = JsonSerializer.Deserialize<PlayerInfoMessage>(json);
            if (playerInfoMessage.players.Length > 0)
                // payload with players
                for (int i = 0; i < playerInfoMessage.players.Length; i++)
                    OnNewPlayer(playerInfoMessage.players[i]);
            else OnNewPlayer(playerInfoMessage);
        }

        private void OnGameData(string json)
        {
            var gameInfoMessage = JsonSerializer.Deserialize<GameInfoMessage>(json);
            if (gameInfoMessage.games?.Length > 0)
                // payload with lobbies
                for (int i = 0; i < gameInfoMessage.games.Length; i++)
                    OnNewGame(gameInfoMessage.games[i]);
            else OnNewGame(gameInfoMessage);

            //AppDebugger.LOGLobby(json.ToJsonFormat());
        }
        private void OnMatchmakerData(string json) => OnMatchMakerDataReceived(JsonSerializer.Deserialize<MatchMakerData>(json));

        // VAULTS
        private void OnMapVaultData(string json)
        {

        }

        private void OnGameLaunchData(string json) => OnGameLaunchDataReceived(JsonSerializer.Deserialize<GameLaunchData>(json));

        private void OnPing(string json = null) => Client.Write(ServerCommands.Pong);

        private void OnPong(string json = null)
        {
            WaitingPong = false;

            //Logger.LogInformation($"Received PONG, time elapsed: {Stopwatch.Elapsed.ToString("c")}");
            //Stopwatch.Stop();
            AppDebugger.LOGLobby($"Received PONG. TIME ELAPSED: {Stopwatch.Elapsed:c}");

            Stopwatch.Reset();
        } 
        #endregion
    }
}