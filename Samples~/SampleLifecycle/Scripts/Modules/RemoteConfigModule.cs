using System;
using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Modules
{
    /// <summary>
    /// This is a sample initialization async module.
    /// Awaiting process before scene starts.
    /// In this sample, remote config request GameId in GameSettings,
    ///     module must depend on GameSettings to confirm data is ready.
    /// </summary>
    [LifecycleModule(ModulePhase.Core, DependsOn = new[] { typeof(GameSettings) })]
    internal sealed class RemoteConfigModule : AsyncModuleBase
    {
        public RemoteData Data { get; private set; }

        private GameSettings _gameSettings;
        
        private bool initializing;
        private bool DataReady => Data != null && _gameSettings != null;
        
        public override async Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (initializing || DataReady)
                return;

            try
            {
                _gameSettings = await ModuleRegistry.WhenReadyAsync<GameSettings>(cancellationToken);
                
                if (_gameSettings == null)
                    throw new Exception("GameSettings not found");
                
                Data = await GetDummyAsync(_gameSettings.GameId, cancellationToken);
            
                Debug.Log($"Module Initialized: {nameof(RemoteConfigModule)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize remote config module {nameof(RemoteConfigModule)}\n{e.Message}");
            }
        }

        public override void Shutdown()
        {
            Data = null;
            
            Debug.Log($"Module Shutdown: {nameof(RemoteConfigModule)}");
        }

        private static async Task<RemoteData> GetDummyAsync(string gameId, CancellationToken token)
        {
            Debug.Log($"Getting dummy data for {gameId}");
            
            var getTime = 3;
            while (getTime > 0)
            {
                Debug.Log($"Getting Remote Data ... {getTime}");
                await Task.Delay(1000, token);
                getTime--;
            }

            return token.IsCancellationRequested
                ? throw new TaskCanceledException()
                : new RemoteData("iAmToken", "good", 12);
        }
    }

    internal sealed class RemoteData
    {
        public RemoteData(string accessToken, string serverState, int gatewayId)
        {
            AccessToken = accessToken;
            ServerState = serverState;
            GatewayId = gatewayId;
        }

        public string AccessToken { get; }
        public string ServerState { get; }
        public int GatewayId { get; }
    }
}