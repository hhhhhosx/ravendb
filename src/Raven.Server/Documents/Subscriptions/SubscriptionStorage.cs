﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents.Exceptions.Subscriptions;
using Raven.Client.Documents.Subscriptions;
using Raven.Server.Documents.TcpHandlers;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow.Json.Parsing;
using Sparrow.Logging;
using Raven.Server.ServerWide.Commands.Subscriptions;
using Raven.Client.Documents.Replication.Messages;
using System.Threading.Tasks;
using Raven.Client.Json.Converters;
using Raven.Client.Server;
using Raven.Server.Rachis;
using Raven.Server.Utils;
using Sparrow.Json;

namespace Raven.Server.Documents.Subscriptions
{
    // todo: implement functionality for limiting amount of opened subscriptions
    public class SubscriptionStorage : IDisposable
    {
        private readonly DocumentDatabase _db;
        private readonly ServerStore _serverStore;
        public static TimeSpan TwoMinutesTimespan = TimeSpan.FromMinutes(2);
        private readonly ConcurrentDictionary<long, SubscriptionConnectionState> _subscriptionStates = new ConcurrentDictionary<long, SubscriptionConnectionState>();
        private readonly Logger _logger;

        public SubscriptionStorage(DocumentDatabase db, ServerStore serverStore)
        {
            _db = db;
            _serverStore = serverStore;
            _logger = LoggingSource.Instance.GetLogger<SubscriptionStorage>(db.Name);
        }

        public void Dispose()
        {
            var aggregator = new ExceptionAggregator(_logger, "Error disposing SubscriptionStorage");
            foreach (var state in _subscriptionStates.Values)
            {
                aggregator.Execute(state.Dispose);
            }
            aggregator.ThrowIfNeeded();
        }

        public void Initialize()
        {

        }

        public async Task<long> PutSubscription(SubscriptionCreationOptions options, long? subscriptionId = null)
        {
            var command = new PutSubscriptionCommand(_db.Name)
            {
                Criteria = options.Criteria,
                InitialChangeVector = options.ChangeVector,
                SubscriptionName = options.Name,
                SubscriptionId = subscriptionId
            };

            var (etag, _) = await _serverStore.SendToLeaderAsync(command);

            if (_logger.IsInfoEnabled)
                _logger.Info($"New Subscription with index {etag} was created");

            await _db.RachisLogIndexNotifications.WaitForIndexNotification(etag);
            return etag;
        }

        public SubscriptionConnectionState OpenSubscription(SubscriptionConnection connection)
        {
            var subscriptionState = _subscriptionStates.GetOrAdd(connection.SubscriptionId,
                subscriptionId => new SubscriptionConnectionState(subscriptionId, this));
            return subscriptionState;
        }

        public async Task AcknowledgeBatchProcessed(long id, long lastEtag, ChangeVectorEntry[] changeVector)
        {
            var changeVectorForEtag = changeVector;

            var command = new AcknowledgeSubscriptionBatchCommand(_db.Name)
            {
                ChangeVector = changeVectorForEtag,
                NodeTag = _serverStore.NodeTag,
                SubscriptionId = id,
                LastDocumentEtagAckedInNode = lastEtag,
                DbId = _db.DbId
            };

            var (etag, _) = await _serverStore.SendToLeaderAsync(command);
            await _db.RachisLogIndexNotifications.WaitForIndexNotification(etag);
        }

        public SubscriptionState GetSubscriptionFromServerStore(long id)
        {
            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext serverStoreContext))
            using (serverStoreContext.OpenReadTransaction())
            {
                return GetSubscriptionFromServerStore(serverStoreContext, id);
            }
        }

        public async Task AssertSubscriptionIdIsApplicable(long id, TimeSpan timeout)
        {
            await _serverStore.WaitForCommitIndexChange(RachisConsensus.CommitIndexModification.GreaterOrEqual, id);

            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext serverStoreContext))
            using (serverStoreContext.OpenReadTransaction())
            {
                var subscription = GetSubscriptionFromServerStore(serverStoreContext, id);

                var dbRecord = _serverStore.Cluster.ReadDatabase(serverStoreContext, _db.Name, out var _);
                var whoseTaskIsIt = dbRecord.Topology.WhoseTaskIsIt(subscription,_serverStore.IsPassive());
                if (whoseTaskIsIt != _serverStore.NodeTag)
                {
                    throw new SubscriptionDoesNotBelongToNodeException($"Subscripition with id {id} can't be proccessed on current node ({_serverStore.NodeTag}), because it belongs to {whoseTaskIsIt}")
                    {
                        AppropriateNode = whoseTaskIsIt
                    };
                }
                if(subscription.Disabled)
                    throw new SubscriptionClosedException($"The subscription {id} is disabled and cannot be used until enabled");
            }
        }

        public async Task DeleteSubscription(long id)
        {
            var command = new DeleteSubscriptionCommand(_db.Name)
            {
                SubscriptionId = id
            };

            var (etag, _) = await _serverStore.SendToLeaderAsync(command);

            if (_logger.IsInfoEnabled)
            {
                _logger.Info($"Subscription with id {id} was deleted");
            }
            await _db.RachisLogIndexNotifications.WaitForIndexNotification(etag);
        }

        public bool DropSubscriptionConnection(long subscriptionId, SubscriptionException ex)
        {
            SubscriptionConnectionState subscriptionConnectionState;
            if (_subscriptionStates.TryGetValue(subscriptionId, out subscriptionConnectionState) == false)
                return false;

            subscriptionConnectionState.Connection.ConnectionException = ex;
            subscriptionConnectionState.RegisterRejectedConnection(subscriptionConnectionState.Connection, ex);
            subscriptionConnectionState.Connection.CancellationTokenSource.Cancel();

            if (_logger.IsInfoEnabled)
                _logger.Info($"Subscription with id {subscriptionId} connection was dropped. Reason: {ex.Message}");

            return true;
        }


        public bool RedirectSubscriptionConnection(long subscriptionId, string reason)
        {
            SubscriptionConnectionState subscriptionConnectionState;
            if (_subscriptionStates.TryGetValue(subscriptionId, out subscriptionConnectionState) == false)
                return false;

            subscriptionConnectionState.Connection.ConnectionException = new SubscriptionDoesNotBelongToNodeException(reason);
            subscriptionConnectionState.RegisterRejectedConnection(subscriptionConnectionState.Connection, new SubscriptionDoesNotBelongToNodeException(reason));
            subscriptionConnectionState.Connection.CancellationTokenSource.Cancel();

            if (_logger.IsInfoEnabled)
                _logger.Info($"Subscription with id {subscriptionId} connection was dropped. Reason: {reason}");

            return true;
        }

        public IEnumerable<SubscriptionGeneralDataAndStats> GetAllSubscriptions(TransactionOperationContext serverStoreContext, bool history, int start, int take)
        {
            foreach (var keyValue in ClusterStateMachine.ReadValuesStartingWith(serverStoreContext,
                SubscriptionState.SubscriptionPrefix(_db.Name)))
            {
                var subscriptionState = JsonDeserializationClient.SubscriptionState(keyValue.Value);
                var subscriptionGeneralData = new SubscriptionGeneralDataAndStats(subscriptionState);
                GetSubscriptionInternal(subscriptionGeneralData, history);
                yield return subscriptionGeneralData;
            }
        }

        public IEnumerable<SubscriptionGeneralDataAndStats> GetAllRunningSubscriptions(TransactionOperationContext context, bool history, int start, int take)
        {

            foreach (var kvp in _subscriptionStates)
            {
                var subscriptionState = kvp.Value;
                var subscriptionId = kvp.Key;

                if (subscriptionState?.Connection == null)
                    continue;

                if (start > 0)
                {
                    start--;
                    continue;
                }

                if (take-- <= 0)
                    yield break;

                var subscriptionData = GetSubscriptionFromServerStore(context, subscriptionId);
                GetRunningSubscriptionInternal(history, subscriptionData, subscriptionState);
                yield return subscriptionData;
            }
        }

        public SubscriptionGeneralDataAndStats GetSubscription(TransactionOperationContext context, long id, bool history)
        {
            var subscription = GetSubscriptionFromServerStore(context, id);

            GetSubscriptionInternal(subscription, history);

            return subscription;
        }

        public SubscriptionGeneralDataAndStats GetSubscriptionFromServerStore(TransactionOperationContext context, long id)
        {
            var subscriptionBlittable = _serverStore.Cluster.Read(context, SubscriptionState.GenerateSubscriptionItemNameFromId(_db.Name, id));

            if (subscriptionBlittable == null)
                throw new SubscriptionDoesNotExistException($"Subscripiton with id {id} was not found in server store");

            var subscriptionState = JsonDeserializationClient.SubscriptionState(subscriptionBlittable);
            var subscriptionJsonValue = new SubscriptionGeneralDataAndStats(subscriptionState);
            return subscriptionJsonValue;
        }

        public SubscriptionGeneralDataAndStats GetRunningSubscription(TransactionOperationContext context, long id, bool history)
        {
            SubscriptionConnectionState subscriptionConnectionState;
            if (_subscriptionStates.TryGetValue(id, out subscriptionConnectionState) == false)
                return null;

            if (subscriptionConnectionState.Connection == null)
                return null;

            var subscriptionJsonValue = GetSubscriptionFromServerStore(context, id);
            GetRunningSubscriptionInternal(history, subscriptionJsonValue, subscriptionConnectionState);
            return subscriptionJsonValue;
        }
        public class SubscriptionGeneralDataAndStats : SubscriptionState
        {
            public SubscriptionConnection Connection;
            public SubscriptionConnection[] RecentConnections;
            public SubscriptionConnection[] RecentRejectedConnections;

            public SubscriptionGeneralDataAndStats() { }

            public SubscriptionGeneralDataAndStats(SubscriptionState @base)
            {
                Criteria = @base.Criteria;
                ChangeVector = @base.ChangeVector;
                SubscriptionId = @base.SubscriptionId;
            }
        }
        public SubscriptionGeneralDataAndStats GetRunningSubscriptionConnectionHistory(TransactionOperationContext context, long subscriptionId)
        {
            SubscriptionConnectionState subscriptionConnectionState;
            if (!_subscriptionStates.TryGetValue(subscriptionId, out subscriptionConnectionState))
                return null;

            var subscriptionConnection = subscriptionConnectionState.Connection;
            if (subscriptionConnection == null)
                return null;

            var subscriptionData = GetSubscriptionFromServerStore(context, subscriptionId);
            subscriptionData.Connection = subscriptionConnectionState.Connection;
            SetSubscriptionHistory(subscriptionConnectionState, subscriptionData);

            return subscriptionData;
        }

        public long GetRunningCount()
        {
            return _subscriptionStates.Count(x => x.Value.Connection != null);
        }

        public long GetAllSubscriptionsCount()
        {
            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                return ClusterStateMachine.ReadValuesStartingWith(context, SubscriptionState.SubscriptionPrefix(_db.Name))
                    .Count();
            }
        }

        private void SetSubscriptionHistory(SubscriptionConnectionState subscriptionConnectionState, SubscriptionGeneralDataAndStats subscriptionData)
        {
            subscriptionData.RecentConnections = subscriptionConnectionState.RecentConnections;
            subscriptionData.RecentRejectedConnections = subscriptionConnectionState.RecentRejectedConnections;
        }

        private void GetRunningSubscriptionInternal(bool history, SubscriptionGeneralDataAndStats subscriptionData, SubscriptionConnectionState subscriptionConnectionState)
        {
            subscriptionData.Connection = subscriptionConnectionState.Connection;
            if (history) // TODO: Only valid for this node
                SetSubscriptionHistory(subscriptionConnectionState, subscriptionData);
        }

        private void GetSubscriptionInternal(SubscriptionGeneralDataAndStats subscriptionData, bool history)
        {
            SubscriptionConnectionState subscriptionConnectionState;
            if (_subscriptionStates.TryGetValue(subscriptionData.SubscriptionId, out subscriptionConnectionState))
            {
                subscriptionData.Connection = subscriptionConnectionState.Connection;

                if (history)//TODO: Only valid if this is my subscription
                    SetSubscriptionHistory(subscriptionConnectionState, subscriptionData);
            }
        }

        public void HandleDatabaseValueChange(DatabaseRecord databaseRecord)
        {
            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                foreach (var subscriptionStateKvp in _subscriptionStates)
                {
                    var subscriptionBlittable = _serverStore.Cluster.Read(context, SubscriptionState.GenerateSubscriptionItemNameFromId(databaseRecord.DatabaseName, subscriptionStateKvp.Key));
                    if (subscriptionBlittable == null)
                    {
                        DropSubscriptionConnection(subscriptionStateKvp.Key, new SubscriptionDoesNotExistException("Deleted"));
                        continue;
                    }
                    var subscriptionState = JsonDeserializationClient.SubscriptionState(subscriptionBlittable);


                    if (databaseRecord.Topology.WhoseTaskIsIt(subscriptionState,_serverStore.IsPassive()) != _serverStore.NodeTag)
                    {
                        if (_logger.IsInfoEnabled)
                            _logger.Info($"Disconnected subscription with id {subscriptionStateKvp.Key}, because it was is no longer managed by this node ({_serverStore.NodeTag})");
                        RedirectSubscriptionConnection(subscriptionStateKvp.Key, "Subscription operation was stopped, because it's now under different server's responsibility");
                    }
                }
            }
        }

        public Task GetSubscriptionConnectionInUseAwaiter(long subscriptionId)
        {
            if (_subscriptionStates.TryGetValue(subscriptionId, out SubscriptionConnectionState state) == false)
                return Task.CompletedTask;

            return state.ConnectionInUse.WaitAsync();
        }
    }
}
