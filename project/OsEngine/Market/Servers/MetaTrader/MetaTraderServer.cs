using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.Entity;
using System;
using System.Collections.Generic;
using System.Net;

namespace OsEngine.Market.Servers.MetaTrader
{
    public class MetaTraderServer : AServer
    {
        public MetaTraderServer(int uniqueId)
        {
            ServerNum = uniqueId;

            MetaTraderServerRealization realization = new MetaTraderServerRealization();
            ServerRealization = realization;

        }
    }

    public class MetaTraderServerRealization : IServerRealization
    {

        #region 1 Constructor, Status, Connection

        public MetaTraderServerRealization()
        {
        }

        void IServerRealization.Connect(WebProxy proxy)
        {
            throw new NotImplementedException();
        }

        void IServerRealization.Dispose()
        {
            throw new NotImplementedException();
        }

        public event Action ConnectEvent;

        public event Action DisconnectEvent;

        public DateTime ServerTime { get; set; }

        public ServerConnectStatus ServerStatus { get; set; } = ServerConnectStatus.Disconnect;

        public List<IServerParameter> ServerParameters { get; set; }

        #endregion

        #region 2 Properties

        public ServerType ServerType => ServerType.MetaTrader;

        #endregion

        #region 3 Securities
        void IServerRealization.GetSecurities()
        {
            throw new NotImplementedException();
        }

        public event Action<List<Security>> SecurityEvent;

        private List<Security> _securities = new List<Security>();

        #endregion

        #region 4 Portfolios

        public void GetPortfolios()
        {
            //PortfolioEvent?.Invoke(_myPortfolios);
        }


        public event Action<List<Portfolio>> PortfolioEvent;

        #endregion

        #region 5 Data
        public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)
        {
            List<Candle> candles = new List<Candle>();

            return candles;
        }

        List<Candle> IServerRealization.GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        List<Trade> IServerRealization.GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region 6 Streams creation

        #endregion

        #region 7 Security subscribe

        void IServerRealization.Subscribe(Security security)
        {
            throw new NotImplementedException();
        }

        public bool SubscribeNews()
        {
            return false;
        }

        public event Action<News> NewsEvent;

        #endregion

        #region 8 Reading messages from data streams



        public event Action<OptionMarketDataForConnector> AdditionalMarketDataEvent;

        public event Action<MarketDepth> MarketDepthEvent;

        public event Action<Trade> NewTradesEvent;

        public event Action<MyTrade> MyTradeEvent;

        #endregion

        #region 9 Channel check alive

        #endregion

        #region 10 Trade

        public void SendOrder(Order order)
        {

        }


        public bool CancelOrder(Order order)
        {
            return false;
        }

        public OrderStateType GetOrderStatus(Order order)
        {

            return order.State;
        }

        void IServerRealization.GetAllActivOrders()
        {
            throw new NotImplementedException();
        }

        public void CancelAllOrders()
        {

        }

        public void CancelAllOrdersToSecurity(Security security)
        {
        }


        public void ChangeOrderPrice(Order order, decimal newPrice) { }

        public event Action<Order> MyOrderEvent;
        #endregion

        #region 11 Helpers

        public void SetDisconnected()
        {
            if (ServerStatus != ServerConnectStatus.Disconnect)
            {
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent?.Invoke();
            }
        }

        public void SetСonnected()
        {
            if (ServerStatus != ServerConnectStatus.Connect)
            {
                ServerStatus = ServerConnectStatus.Connect;
                ConnectEvent?.Invoke();
            }
        }

        #endregion

        #region 12 Log

        private void SendLogMessage(string msg, LogMessageType msgType)
        {
            LogMessageEvent?.Invoke(msg, msgType);
        }

        public event Action<string, LogMessageType> LogMessageEvent;

        public event Action<Funding> FundingUpdateEvent;

        public event Action<SecurityVolumes> Volume24hUpdateEvent;

        #endregion
    }
}