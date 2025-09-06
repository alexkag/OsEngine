using MtApi5;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.Entity;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

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

        static readonly EventWaitHandle _connnectionWaiter = new AutoResetEvent(false);
        static readonly MtApi5Client _mtapi = new MtApi5Client();
        public void Connect(WebProxy proxy)
        {
            SendLogMessage("Start MetaTrader Windows terminal connection", LogMessageType.System);

            _mtapi.ConnectionStateChanged += _mtapi_ConnectionStateChanged;
            _mtapi.QuoteAdded += _mtapi_QuoteAdded;
            _mtapi.QuoteRemoved += _mtapi_QuoteRemoved;
            _mtapi.QuoteUpdate += _mtapi_QuoteUpdate;


            _mtapi.BeginConnect(8228);
            _connnectionWaiter.WaitOne();

            if (_mtapi.ConnectionState != Mt5ConnectionState.Connected)
            {
                SendLogMessage("Not connected. Check setup.", LogMessageType.System);
                SetDisconnected();
                return;
            }

            SendLogMessage("Client connected.", LogMessageType.System);
            SetСonnected();
        }

        void _mtapi_ConnectionStateChanged(object sender, Mt5ConnectionEventArgs e)
        {
            switch (e.Status)
            {
                case Mt5ConnectionState.Connecting:
                    SendLogMessage("Connecting...", LogMessageType.System);
                    break;
                case Mt5ConnectionState.Connected:
                    SendLogMessage("Connected.", LogMessageType.System);
                    _connnectionWaiter.Set();
                    break;
                case Mt5ConnectionState.Disconnected:
                    SendLogMessage("Disconnected.", LogMessageType.System);
                    _connnectionWaiter.Set();
                    break;
                case Mt5ConnectionState.Failed:
                    SendLogMessage("Connection failed.", LogMessageType.System);
                    _connnectionWaiter.Set();
                    break;
            }
        }

        void _mtapi_QuoteAdded(object sender, Mt5QuoteEventArgs e)
        {
            //Console.WriteLine("Quote added with symbol {0}", e.Quote.Instrument);
            SendLogMessage("Quote added with symbol " + e.Quote.Instrument, LogMessageType.System);
        }

        void _mtapi_QuoteRemoved(object sender, Mt5QuoteEventArgs e)
        {
            //Console.WriteLine("Quote removed with symbol {0}", e.Quote.Instrument);
            SendLogMessage("Quote removed with symbol " + e.Quote.Instrument, LogMessageType.System);
        }

        void _mtapi_QuoteUpdate(object sender, Mt5QuoteEventArgs e)
        {
            Console.WriteLine("Quote updated: {0} - {1} : {2}", e.Quote.Instrument, e.Quote.Bid, e.Quote.Ask);
            string msg = string.Format("Quote updated: {0} - {1} : {2}", e.Quote.Instrument, e.Quote.Bid, e.Quote.Ask);
            SendLogMessage(msg, LogMessageType.System);
        }


        public void Dispose()
        {
            _mtapi.BeginDisconnect();

            _mtapi.ConnectionStateChanged -= _mtapi_ConnectionStateChanged;
            _mtapi.QuoteAdded -= _mtapi_QuoteAdded;
            _mtapi.QuoteRemoved -= _mtapi_QuoteRemoved;
            _mtapi.QuoteUpdate -= _mtapi_QuoteUpdate;

            if (_mtapi.ConnectionState != Mt5ConnectionState.Disconnected)
            {
                SendLogMessage("Disconnect failed.", LogMessageType.System);
                return;
            }

            SendLogMessage("Connection to MetaTrader Windows terminal closed.", LogMessageType.System);

            SetDisconnected();
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
            IEnumerable<Mt5Quote> secs = _mtapi.GetQuotes();
            int secsCount = _mtapi.SymbolsTotal(false);
            for (int i = 0; i < secsCount; i++)
            {
                Security security = new Security();
                security.Name = _mtapi.SymbolName(i, false);
                //security.Name = "VTBR";
                security.Lot = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_TRADE_CONTRACT_SIZE));
                security.PriceStep = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_TRADE_TICK_SIZE));
                security.PriceStepCost = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_TRADE_TICK_SIZE));
                security.PriceLimitLow = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_SESSION_PRICE_LIMIT_MIN));
                security.PriceLimitHigh = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_SESSION_PRICE_LIMIT_MAX));
                security.VolumeStep = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_VOLUME_STEP));
                security.MinTradeAmountType = MinTradeAmountType.C_Currency; //TODO проверить
                //var res1 = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_PATH);
                //var res1 = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_CATEGORY);
                //var res2 = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_BASIS);
                security.NameClass = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_ISIN);
                security.NameFull = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_DESCRIPTION);
                security.NameId = security.Name + "@" + security.NameClass;
                security.Decimals = Convert.ToInt16(_mtapi.SymbolInfoInteger(security.Name, ENUM_SYMBOL_INFO_INTEGER.SYMBOL_DIGITS));
                //var values = Enum.GetValues(typeof(ENUM_SYMBOL_INFO_DOUBLE));
                //SendLogMessage("Double params", LogMessageType.System);
                //foreach (ENUM_SYMBOL_INFO_DOUBLE param in values)
                //{
                //    decimal res = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.Name, param));
                //    string msg = string.Format("Param {0}, value {1}", param, res);
                //    SendLogMessage(msg, LogMessageType.System);
                //}
                _securities.Add(security);
            }

            if (secs == null) return;

            //IEnumerator iSecs = secs.GetEnumerator();
            //while (iSecs.MoveNext())
            //{
            //    Mt5Quote sec = (Mt5Quote)iSecs.Current;
            //    security.Name = sec.Instrument;
            //    security.NameId = sec.Instrument;
            //    security.NameFull = sec.Instrument;
            //    security.NameClass = ServerType.MetaTrader.ToString();
            //    security.State = SecurityStateType.Activ;
            //    security.Exchange = ServerType.MetaTrader.ToString();
            //    _securities.Add(security);
            //}

            SecurityEvent?.Invoke(_securities);

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

        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
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