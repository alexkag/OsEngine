using MtApi5;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market.Servers.Entity;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace OsEngine.Market.Servers.MetaTrader5
{
    public class MetaTrader5Server : AServer
    {
        public MetaTrader5Server(int uniqueId)
        {
            ServerNum = uniqueId;

            MetaTrader5ServerRealization realization = new MetaTrader5ServerRealization();
            ServerRealization = realization;

            CreateParameterString("Host", "localhost");
            CreateParameterInt("Port", 8228);
            //CreateParameterBoolean("Hedge Mode", false);
            CreateParameterString("Securities filter", "moex");
        }
    }

    public class MetaTrader5ServerRealization : IServerRealization
    {

        #region 1 Constructor, Status, Connection

        public MetaTrader5ServerRealization()
        {

        }

        static readonly EventWaitHandle _connnectionWaiter = new AutoResetEvent(false);
        static readonly MtApi5Client _mtapi = new MtApi5Client();
        public void Connect(WebProxy proxy)
        {
            SendLogMessage("Start MetaTrader5 Windows terminal connection", LogMessageType.System);

            _metatraderHost = ((ServerParameterString)ServerParameters[0]).Value;
            _metatraderPort = ((ServerParameterInt)ServerParameters[1]).Value;
            //_isHedgeMode = ((ServerParameterBool)ServerParameters[2]).Value;
            _securitiesFilter = ((ServerParameterString)ServerParameters[2]).Value;

            _mtapi.ConnectionStateChanged += _mtapi_ConnectionStateChanged;
            //_mtapi.QuoteAdded += _mtapi_QuoteAdded;
            //_mtapi.QuoteRemoved += _mtapi_QuoteRemoved;
            _mtapi.QuoteUpdate += NewTradeEventHandler;
            _mtapi.QuoteUpdated += _mtapi_QuoteUpdated;
            _mtapi.OnLockTicks += _mtapi_OnLockTicks;
            //_mtapi.    += NewCandleEventHandler;
            _mtapi.OnTradeTransaction += MyTradeEventHandler;
            _mtapi.OnTradeTransaction += MyOrderEventHandler;
            //_mtapi.QuoteList += MarketDepthEventHandler;
            _mtapi.OnBookEvent += MarketDepthEventHandler;



            _mtapi.BeginConnect(_metatraderHost, _metatraderPort);
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

        private void _mtapi_OnLockTicks(object sender, Mt5LockTicksEventArgs e)
        {
            var msg =
                $"OnLockTicks: Symbol = {e.Symbol}";
            SendLogMessage(msg, LogMessageType.System);
        }

        private void _mtapi_QuoteUpdated(object sender, string symbol, double bid, double ask)
        {
            SendLogMessage("Quote updated.", LogMessageType.System);
        }

        /// <summary>
        /// https://www.mql5.com/ru/docs/constants/structures/mqltradetransaction
        /// https://www.mql5.com/ru/docs/event_handlers/ontradetransaction
        /// Отправка торгового запроса на покупку приводит к цепи торговых транзакций, которые совершаются на торговом счете:
        /// 1) запрос  принимается на обработку,
        /// 2) далее для счета создается соответствующий ордер на покупку,
        /// 3) затем происходит исполнение ордера,
        /// 4) удаление исполненного ордера из списка действующих,
        /// 5) добавление в историю ордеров,
        /// 6) далее добавляется соответствующая сделка в историю и
        /// 7) создается новая позиция.
        /// Все эти действия являются торговыми транзакциями.
        /// Приход каждой такой транзакции в терминал является событием TradeTransaction.
        /// При этом очередность поступления этих транзакций в терминал не гарантирована,
        /// поэтому нельзя свой торговый алгоритм строить на ожидании поступления одних торговых транзакций после прихода других.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MyTradeEventHandler(object sender, Mt5TradeTransactionEventArgs e)
        {
            if (!(e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_DEAL_ADD
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_DEAL_UPDATE
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_DEAL_DELETE
                )) return;

            MyTrade trade = new MyTrade();
            trade.Volume = Convert.ToDecimal(e.Trans.Volume);
            trade.Price = Convert.ToDecimal(e.Trans.Price);
            trade.Side = GetSide(e.Trans.OrderType);
            trade.NumberTrade = e.Trans.Deal.ToString();
            //trade.NumberOrderParent = e.Trans.Position.ToString();
            trade.NumberOrderParent = e.Trans.Order.ToString();
            trade.SecurityNameCode = e.Trans.Symbol;
            trade.Time = DateTime.UtcNow.AddHours(_timezoneOffset);

            if (trade.Price > 0 && trade.Volume > 0)
            {
                MyTradeEvent?.Invoke(trade);
            }
        }

        private void MyOrderEventHandler(object sender, Mt5TradeTransactionEventArgs e)
        {
            if (!(e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_ORDER_ADD
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_ORDER_UPDATE
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_ORDER_DELETE
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_HISTORY_ADD
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_HISTORY_UPDATE
                || e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_HISTORY_DELETE
                )) return;


            Order order = new Order();
            order.SecurityNameCode = e.Trans.Symbol;
            //order.NumberUser = e.Trans.
            order.NumberMarket = e.Trans.Order.ToString();
            order.Price = Convert.ToDecimal(e.Trans.Price);
            order.Side = GetSide(e.Trans.OrderType);
            order.TypeOrder = GetOrderPriceType((long)e.Trans.OrderType);
            order.State = GetOrderStateType((long)e.Trans.OrderState);
            order.ServerType = ServerType.MetaTrader5;
            double volumeTotal = _mtapi.HistoryOrderGetDouble(e.Trans.Order, ENUM_ORDER_PROPERTY_DOUBLE.ORDER_VOLUME_INITIAL);
            order.Volume = Convert.ToDecimal(volumeTotal);
            order.VolumeExecute = Convert.ToDecimal(volumeTotal - e.Trans.Volume); // e.Trans.Volume - текущий объем ордера (не исполненный)
            order.TimeCallBack = DateTime.UtcNow.AddHours(_timezoneOffset);
            order.TimeCallBack = DateTime.UtcNow.AddHours(_timezoneOffset);

            if (e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_ORDER_ADD)
            {
                order.State = OrderStateType.Active;
            }
            else if (e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_ORDER_UPDATE)
            {

            }
            else if (e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_ORDER_DELETE)
            {

            }
            else if (e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_HISTORY_ADD)
            {
                order.State = OrderStateType.Done;
            }
            else if (e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_HISTORY_UPDATE)
            {

            }
            else if (e.Trans.Type == ENUM_TRADE_TRANSACTION_TYPE.TRADE_TRANSACTION_HISTORY_DELETE)
            {

            }

            //if (order.NumberUser == 0)
            //{
            //    return ;
            //}

            if (order.Price > 0 && order.Volume > 0)
            {
                MyOrderEvent?.Invoke(order);
            }
        }

        private void MarketDepthEventHandler(object sender, Mt5BookEventArgs e)
        {
            MarketDepth depth = new MarketDepth();
            //Security security = GetSecurity(e.Symbol);
            depth.SecurityNameCode = e.Symbol;
            depth.Time = DateTime.UtcNow.AddHours(_timezoneOffset);
            //if (security == null) return;
            //depth.SecurityNameCode = security.Name;
            try
            {
                _mtapi.MarketBookGet(e.Symbol, out MqlBookInfo[]? mtBook);
                var x = mtBook;

                if (mtBook == null) return;
                for (int i = 0; i < mtBook.Length; i++)
                {
                    MarketDepthLevel level = new MarketDepthLevel();
                    level.Price = mtBook[i].price;
                    if (mtBook[i].type == ENUM_BOOK_TYPE.BOOK_TYPE_BUY)
                    {
                        level.Bid = mtBook[i].volume;
                        depth.Bids.Add(level);
                    }
                    if (mtBook[i].type == ENUM_BOOK_TYPE.BOOK_TYPE_SELL)
                    {
                        level.Ask = mtBook[i].volume;
                        depth.Asks.Add(level);
                    }
                }

                depth.Asks.Reverse();
            }
            catch (Exception ex)
            {
                SendLogMessage("Market depth. Client disconnected.", LogMessageType.System);
            }

            if (_lastMdTime != DateTime.MinValue &&
                _lastMdTime >= depth.Time)
            {
                depth.Time = _lastMdTime.AddTicks(1);
            }

            _lastMdTime = depth.Time;
            MarketDepthEvent?.Invoke(depth);
        }
        private DateTime _lastMdTime = DateTime.MinValue;

        //private void NewTradeEventHandler(object sender, Mt5LockTicksEventArgs e)
        //{
        //    SendLogMessage($"Tick {e.Symbol}.", LogMessageType.System);
        //}

        private void NewTradeEventHandler(object sender, Mt5QuoteEventArgs quote)
        {
            Mt5Quote mtTrade = quote.Quote;

            bool isSubscribed = false;
            for (int i = 0; i < _subscribedSecurities.Count; i++)
            {
                if (_subscribedSecurities[i].security.NameId == mtTrade.Instrument)
                {
                    isSubscribed = true;
                    break;
                }
            }
            if (!isSubscribed) return;
            Trade trade = new Trade();
            trade.Id = mtTrade.Time.Ticks.ToString();
            trade.Volume = mtTrade.Volume;
            trade.SecurityNameCode = mtTrade.Instrument;
            trade.Price = Convert.ToDecimal(mtTrade.Last);
            trade.Side = Side.None;
            trade.Time = DateTime.UtcNow.AddHours(_timezoneOffset);
            trade.Bid = Convert.ToDecimal(mtTrade.Bid);
            trade.Ask = Convert.ToDecimal(mtTrade.Ask);
            if (mtTrade.Bid == mtTrade.Last)
            {
                trade.Side = Side.Sell;
                trade.Price = Convert.ToDecimal(mtTrade.Bid);
            }
            else if (mtTrade.Ask == mtTrade.Last)
            {
                trade.Side = Side.Buy;
                trade.Price = Convert.ToDecimal(mtTrade.Ask);
            }
            if (trade.Side == Side.None)
            {
                SendLogMessage($"No trade side {trade.ToString()}.", LogMessageType.System);
                return;
            }
            NewTradesEvent?.Invoke(trade);
        }


        //private void NewTradeEventHandler(object sender, string symbol, double bid, double ask)
        //{
        //    List<MqlTick> ticks = _mtapi.CopyTicks(e.Symbol, CopyTicksFlag.Trade, 0, 1);
        //    if (ticks == null || ticks.Count == 0)
        //    {
        //        return;
        //    }

        //    MqlTick tick = ticks[ticks.Count - 1];

        //Trade trade = new Trade();
        //    trade.Volume = tick.volume;
        //    trade.Side = Side.None;
        //    if (tick.bid > 0)
        //    {
        //        trade.Side = Side.Sell;
        //        trade.Price = Convert.ToDecimal(tick.bid);
        //    }
        //    else if (tick.ask > 0)
        //    {
        //        trade.Side = Side.Buy;
        //        trade.Price = Convert.ToDecimal(tick.ask);
        //    }
        //    //e.Symbol
        //    NewTradesEvent?.Invoke(trade);
        //}

        //private void NewTradeEventHandler(object sender, Mt5LockTicksEventArgs e)
        //{

        //}

        //private void MarketDepthEventHandler(object sender, Mt5QuotesEventArgs e)
        //{
        //    MarketDepth depth = new MarketDepth();
        //    //depth.SecurityNameCode = e.;
        //    //depth.Time = ConvertToDateTimeFromUnixFromMilliseconds(baseMessage.data.ms_timestamp);
        //    for (int i = 0; i < e.Quotes.Count(); i++)
        //    {
        //        Mt5Quote mtLevel = e.Quotes.ElementAt(i);
        //        if (string.IsNullOrEmpty(depth.SecurityNameCode))
        //        {
        //            depth.SecurityNameCode = mtLevel.Instrument;
        //            depth.Time = mtLevel.Time;
        //        }
        //        MarketDepthLevel level = new MarketDepthLevel();
        //        if (mtLevel.Bid > 0)
        //        {
        //            level.Price = Convert.ToDecimal(mtLevel.Bid);
        //            level.Bid = mtLevel.Volume;
        //        }
        //        else if (mtLevel.Ask > 0)
        //        {
        //            level.Price = Convert.ToDecimal(mtLevel.Ask);
        //            level.Ask = mtLevel.Volume;
        //        }
        //    }


        //    if ((depth.Bids != null && depth.Bids.Count > 0) || (depth.Asks != null && depth.Asks.Count > 0))
        //    {
        //        MarketDepthEvent?.Invoke(depth);
        //    }
        //}

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
                    SetСonnected();
                    break;
                case Mt5ConnectionState.Disconnected:
                    SendLogMessage("Disconnected.", LogMessageType.System);
                    _connnectionWaiter.Set();
                    SetDisconnected();
                    break;
                case Mt5ConnectionState.Failed:
                    SendLogMessage("Connection failed.", LogMessageType.System);
                    _connnectionWaiter.Set();
                    SetDisconnected();
                    break;
            }
        }

        //void _mtapi_QuoteAdded(object sender, Mt5QuoteEventArgs e)
        //{
        //    //Console.WriteLine("Quote added with symbol {0}", e.Quote.Instrument);
        //    SendLogMessage("Quote added with symbol " + e.Quote.Instrument, LogMessageType.System);
        //}

        //void _mtapi_QuoteRemoved(object sender, Mt5QuoteEventArgs e)
        //{
        //    //Console.WriteLine("Quote removed with symbol {0}", e.Quote.Instrument);
        //    SendLogMessage("Quote removed with symbol " + e.Quote.Instrument, LogMessageType.System);
        //}

        //void _mtapi_QuoteUpdate(object sender, Mt5QuoteEventArgs e)
        //{
        //    string msg = string.Format("Quote updated: {0} - {1} : {2}", e.Quote.Instrument, e.Quote.Bid, e.Quote.Ask);
        //    SendLogMessage(msg, LogMessageType.System);
        //}


        public void Dispose()
        {
            for (int i = 0; i < _subscribedSecurities.Count; i++)
            {
                Unsubscribe(_subscribedSecurities[i].security);
            }

            _subscribedSecurities.Clear();

            _mtapi.BeginDisconnect();
            //_connnectionWaiter.WaitOne();

            _mtapi.ConnectionStateChanged -= _mtapi_ConnectionStateChanged;
            //_mtapi.QuoteAdded -= _mtapi_QuoteAdded;
            //_mtapi.QuoteRemoved -= _mtapi_QuoteRemoved;
            //_mtapi.QuoteUpdate -= _mtapi_QuoteUpdate;
            //_mtapi.OnLockTicks -= NewTradeEventHandler;
            _mtapi.QuoteUpdate -= NewTradeEventHandler;
            _mtapi.OnTradeTransaction -= MyTradeEventHandler;
            _mtapi.OnTradeTransaction -= MyOrderEventHandler;
            _mtapi.OnBookEvent -= MarketDepthEventHandler;

            if (_mtapi.ConnectionState != Mt5ConnectionState.Disconnected)
            {
                SendLogMessage("Disconnect failed.", LogMessageType.System);
                return;
            }

            SendLogMessage("Connection to MetaTrader5 Windows terminal closed.", LogMessageType.System);

            SetDisconnected();
        }

        public event Action ConnectEvent;

        public event Action DisconnectEvent;

        public DateTime ServerTime { get; set; }

        public ServerConnectStatus ServerStatus { get; set; } = ServerConnectStatus.Disconnect;

        public List<IServerParameter> ServerParameters { get; set; }

        #endregion

        #region 2 Properties

        public ServerType ServerType => ServerType.MetaTrader5;
        private string _metatraderHost;
        private int _metatraderPort;
        private string _securitiesFilter;
        private string _accountId;
        //private bool _isHedgeMode = false;
        private int _timezoneOffset = 3;
        #endregion

        #region 3 Securities
        public void GetSecurities()
        {
            _securities = new List<Security>();

            try
            {
                IEnumerable<Mt5Quote> secs = _mtapi.GetQuotes();
                int securitiesCount = _mtapi.SymbolsTotal(false);
                for (int i = 0; i < securitiesCount; i++)
                {
                    Security security = new Security();
                    security.Exchange = ServerType.MetaTrader5.ToString();
                    security.NameId = _mtapi.SymbolName(i, false);
                    security.Name = security.NameId;
                    security.NameClass = _mtapi.SymbolInfoString(security.NameId, ENUM_SYMBOL_INFO_STRING.SYMBOL_ISIN);
                    if (string.IsNullOrEmpty(security.NameClass))
                    {
                        continue;
                        security.NameClass = "Other";
                    }

                    // Используем фильтр бумаг, если задан
                    // 15 тыс. тикеров Финам - неудобно
                    if (
                        !string.IsNullOrEmpty(_securitiesFilter) &&
                        !(security.NameId.Contains(_securitiesFilter, StringComparison.OrdinalIgnoreCase) || security.NameClass.Contains(_securitiesFilter, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    //security.Name = "VTBR";
                    security.Lot = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.NameId, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_TRADE_CONTRACT_SIZE));
                    security.PriceLimitLow = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.NameId, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_SESSION_PRICE_LIMIT_MIN));
                    security.PriceLimitHigh = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.NameId, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_SESSION_PRICE_LIMIT_MAX));
                    security.VolumeStep = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.NameId, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_VOLUME_STEP));
                    //var res1 = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_PATH);
                    //var res1 = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_CATEGORY);
                    //var res2 = _mtapi.SymbolInfoString(security.Name, ENUM_SYMBOL_INFO_STRING.SYMBOL_BASIS);
                    //try
                    //{
                    //    security.NameFull = _mtapi.SymbolInfoString(security.NameId, ENUM_SYMBOL_INFO_STRING.SYMBOL_DESCRIPTION) ?? security.Name + "@" + security.NameClass;
                    //}
                    //catch (Exception ex)
                    //{
                    //    SendLogMessage($"Get Security data error. Security {security.NameId}. Index {i}.", LogMessageType.Error);
                    //    //security.Name = "Other";
                    //}
                    security.NameFull = security.NameId;// + "@" + security.NameClass;
                    security.PriceStep = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.NameId, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_TRADE_TICK_SIZE));
                    security.PriceStepCost = security.PriceStep;
                    //security.PriceStepCost = Convert.ToDecimal(_mtapi.SymbolInfoDouble(security.NameId, ENUM_SYMBOL_INFO_DOUBLE.SYMBOL_TRADE_TICK_SIZE));
                    //security.Decimals = Convert.ToInt16(_mtapi.SymbolInfoInteger(security.Name, ENUM_SYMBOL_INFO_INTEGER.SYMBOL_DIGITS));
                    security.Decimals = GetDecimals(security.PriceStep);
                    security.State = SecurityStateType.Activ;

                    // Платформо зависимо
                    if (security.NameClass.ToLower().Contains("stock"))
                    {
                        security.SecurityType = SecurityType.Stock;
                        security.MinTradeAmountType = MinTradeAmountType.Contract;
                    }
                    else if (security.NameClass.ToLower().Contains("fut"))
                    {
                        security.SecurityType = SecurityType.Futures;
                        security.MinTradeAmountType = MinTradeAmountType.Contract;
                    }
                    else if (security.NameClass.ToLower().Contains("cur"))
                    {
                        security.SecurityType = SecurityType.CurrencyPair;
                        security.MinTradeAmountType = MinTradeAmountType.C_Currency;
                    }
                    else
                    {
                        //security.SecurityType = SecurityType.Index;
                        security.SecurityType = SecurityType.Stock;
                        security.MinTradeAmountType = MinTradeAmountType.Contract;
                    }


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
            }
            catch (Exception e)
            {
                SendLogMessage("Get Securities error. " + e.Message, LogMessageType.Error);
            }

            if (_mtapi.ConnectionState != Mt5ConnectionState.Connected)
            {
                SendLogMessage("Not connected. Check setup.", LogMessageType.System);
                SetDisconnected();
                return;
            }


            if (_securities == null) return;

            //IEnumerator iSecs = secs.GetEnumerator();
            //while (iSecs.MoveNext())
            //{
            //    Mt5Quote sec = (Mt5Quote)iSecs.Current;
            //    security.Name = sec.Instrument;
            //    security.NameId = sec.Instrument;
            //    security.NameFull = sec.Instrument;
            //    security.NameClass = ServerType.MetaTrader5.ToString();
            //    security.State = SecurityStateType.Activ;
            //    security.Exchange = ServerType.MetaTrader5.ToString();
            //    _securities.Add(security);
            //}

            SecurityEvent?.Invoke(_securities);
            SendLogMessage("Securities list was loaded.", LogMessageType.System);

        }

        public event Action<List<Security>> SecurityEvent;

        private List<Security> _securities = new List<Security>();

        #endregion

        #region 4 Portfolios

        /// <summary>
        /// https://www.mql5.com/ru/docs/constants/environment_state/accountinformation
        /// </summary>
        public void GetPortfolios()
        {
            _accountId = _mtapi.AccountInfoInteger(ENUM_ACCOUNT_INFO_INTEGER.ACCOUNT_LOGIN).ToString();
            Portfolio myPortfolio = _myPortfolios.Find(p => p.Number == _accountId);

            if (myPortfolio == null)
            {
                myPortfolio = new Portfolio();
                myPortfolio.ServerType = ServerType.MetaTrader5;
                myPortfolio.Number = _accountId;
                SendLogMessage("Account Leverage: " + _mtapi.AccountInfoInteger(ENUM_ACCOUNT_INFO_INTEGER.ACCOUNT_LEVERAGE), LogMessageType.System);
                SendLogMessage("Account Currency: " + _mtapi.AccountInfoString(ENUM_ACCOUNT_INFO_STRING.ACCOUNT_CURRENCY), LogMessageType.System);
                //SendLogMessage("Account Assets: " + _mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_ASSETS), LogMessageType.System);
                myPortfolio.ValueCurrent = Convert.ToDecimal(_mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_BALANCE) + _mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_ASSETS));
                myPortfolio.ValueBegin = myPortfolio.ValueCurrent;
                myPortfolio.ValueBlocked = Convert.ToDecimal(_mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_COMMISSION_BLOCKED));
                myPortfolio.UnrealizedPnl = Convert.ToDecimal(_mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_PROFIT));

                _myPortfolios.Add(myPortfolio);
            }
            else
            {
                myPortfolio.ValueCurrent = Convert.ToDecimal(_mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_BALANCE));
                myPortfolio.ValueBegin = myPortfolio.ValueCurrent;
                //myPortfolio.ValueBlocked = Convert.ToDecimal(_mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_COMMISSION_BLOCKED));
                myPortfolio.UnrealizedPnl = Convert.ToDecimal(_mtapi.AccountInfoDouble(ENUM_ACCOUNT_INFO_DOUBLE.ACCOUNT_PROFIT));
            }

            decimal valueBlocked = 0;
            long positionsCount = _mtapi.PositionsTotal();
            for (int i = 0; i < positionsCount; i++)
            {
                PositionOnBoard position = new PositionOnBoard();
                position.PortfolioName = myPortfolio.Number;
                position.SecurityNameCode = _mtapi.PositionGetSymbol(i);
                position.ValueCurrent = Convert.ToDecimal(_mtapi.PositionGetDouble(ENUM_POSITION_PROPERTY_DOUBLE.POSITION_PRICE_CURRENT) * _mtapi.PositionGetDouble(ENUM_POSITION_PROPERTY_DOUBLE.POSITION_VOLUME));
                position.ValueBegin = position.ValueCurrent;
                position.ValueBlocked = position.ValueCurrent;
                position.UnrealizedPnl = Convert.ToDecimal(_mtapi.PositionGetDouble(ENUM_POSITION_PROPERTY_DOUBLE.POSITION_PROFIT));
                myPortfolio.SetNewPosition(position);

                valueBlocked += position.ValueBlocked;
            }

            myPortfolio.ValueBlocked = valueBlocked;
            PortfolioEvent?.Invoke(_myPortfolios);
        }


        public event Action<List<Portfolio>> PortfolioEvent;
        private List<Portfolio> _myPortfolios = new List<Portfolio>();
        #endregion

        #region 5 Data
        public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)
        {
            DateTime timeStart = DateTime.UtcNow.AddHours(_timezoneOffset) - TimeSpan.FromMinutes(timeFrameBuilder.TimeFrameTimeSpan.TotalMinutes * candleCount);
            DateTime timeEnd = DateTime.UtcNow.AddHours(_timezoneOffset);

            List<Candle> candles = GetCandleDataToSecurity(security, timeFrameBuilder, timeStart, timeEnd, timeStart);
            //List<Candle> candles = new List<Candle>();

            return candles;
        }

        // Глубина истории также 100 тыс. свечей. Ограничение платформы.
        const int LIMIT_HISTORY_DEPTH = 100000;
        const int LIMIT_HISTORY_REQUEST_CANDLES_COUNT = 10000;
        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            if (startTime != actualTime)
            {
                startTime = actualTime;
            }

            List<Candle> candles = new List<Candle>();
            ENUM_TIMEFRAMES mtTf = GetMtTimeFrame(timeFrameBuilder.TimeFrame);
            //DateTime last = _mtapi.CopyRates(security.NameId, mtTf, qStartTime, requestCandlesCount, out MqlRates[]? mtCandles);
            //if ((DateTime.UtcNow.AddHours(_timezoneOffset) - endTime) / timeFrameBuilder.TimeFrameTimeSpan >= LIMIT_HISTORY_DEPTH)
            //{
            //    return candles;
            //}

            DateTime qStartTime = startTime;
            //DateTime qEndTime;
            //if ((DateTime.UtcNow.AddHours(_timezoneOffset) - qStartTime) / timeFrameBuilder.TimeFrameTimeSpan >= LIMIT_HISTORY_DEPTH)
            //{
            //    qStartTime = DateTime.UtcNow.AddHours(_timezoneOffset) - timeFrameBuilder.TimeFrameTimeSpan * (LIMIT_HISTORY_DEPTH - 1);
            //}

            //if (qStartTime > endTime) return candles;

            //int candlesTotal = (int)((endTime - qStartTime) / timeFrameBuilder.TimeFrameTimeSpan);
            DateTime qEndTime = endTime + timeFrameBuilder.TimeFrameTimeSpan * LIMIT_HISTORY_REQUEST_CANDLES_COUNT;
            while (qStartTime < qEndTime)
            //while (candlesTotal > 0)
            {
                DateTime qqEndTime = qStartTime + timeFrameBuilder.TimeFrameTimeSpan * LIMIT_HISTORY_REQUEST_CANDLES_COUNT;
                //int requestCandlesCount = (qqEndTime > endTime) ? (int)((endTime - qStartTime) / timeFrameBuilder.TimeFrameTimeSpan) + 1 : LIMIT_HISTORY_REQUEST_CANDLES_COUNT;

                //if (candlesTotal > LIMIT_HISTORY_REQUEST_CANDLES_COUNT)
                //{
                //    requestCandlesCount = LIMIT_HISTORY_REQUEST_CANDLES_COUNT;
                //}
                //else
                //{
                //    requestCandlesCount = candlesTotal;
                //}

                try
                {
                    MqlRates[]? mtCandles;
                    if (qqEndTime > endTime)
                    {
                        _mtapi.CopyRates(security.NameId, mtTf, 0, LIMIT_HISTORY_REQUEST_CANDLES_COUNT, out mtCandles);
                    }
                    else
                    {
                        _mtapi.CopyRates(security.NameId, mtTf, qStartTime, LIMIT_HISTORY_REQUEST_CANDLES_COUNT, out mtCandles);
                    }
                    if (!(mtCandles == null || mtCandles.Length == 0))
                    {
                        for (int i = 0; i < mtCandles.Length; i++)
                        {
                            Candle candle = new Candle();
                            candle.Open = mtCandles[i].open.ToString().ToDecimal();
                            candle.Close = mtCandles[i].close.ToString().ToDecimal();
                            candle.High = mtCandles[i].high.ToString().ToDecimal();
                            candle.Low = mtCandles[i].low.ToString().ToDecimal();
                            candle.Volume = mtCandles[i].real_volume.ToString().ToDecimal();
                            candle.TimeStart = mtCandles[i].time;
                            if (
                                candle.TimeStart >= startTime
                                && candle.TimeStart <= endTime
                                && (candles.Count == 0 || candles[candles.Count - 1].TimeStart < candle.TimeStart)
                                )
                            {
                                candles.Add(candle);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // Do nothing
                }

                qStartTime = qStartTime + timeFrameBuilder.TimeFrameTimeSpan * LIMIT_HISTORY_REQUEST_CANDLES_COUNT;
                //candlesTotal -= requestCandlesCount;
            }

            // Максимум 100 тыс. свечей в ответе. Ограничение платформы.
            //double countMinutes = (startTime - endTime) / timeFrameBuilder.TimeFrameTimeSpan;
            //if ((startTime - endTime) / timeFrameBuilder.TimeFrameTimeSpan >= 100000)
            //{
            //    endTime = startTime.AddMinutes(-100000 + 1);
            //}

            //try
            //{
            //    //_mtapi.CopyRates(security.NameId, mtTf, startTime, endTime, out MqlRates[]? mtCandles);
            //    //var rates = _mtapi.CopyRates(security.NameId, mtTf, 0, 1000, out MqlRates[]? mtCandles);
            //    _mtapi.CopyRates(security.NameId, mtTf, qStartTime, candlesCount, out MqlRates[]? mtCandles);
            //    if (mtCandles == null) return candles;
            //    for (int i = 0; i < mtCandles.Length; i++)
            //    {
            //        Candle candle = new Candle();
            //        candle.Open = mtCandles[i].open.ToString().ToDecimal();
            //        candle.Close = mtCandles[i].close.ToString().ToDecimal();
            //        candle.High = mtCandles[i].high.ToString().ToDecimal();
            //        candle.Low = mtCandles[i].low.ToString().ToDecimal();
            //        candle.Volume = mtCandles[i].real_volume.ToString().ToDecimal();
            //        candle.TimeStart = mtCandles[i].time;
            //        if (candle.TimeStart >= startTime && candle.TimeStart <= endTime)
            //        {
            //            candles.Add(candle);
            //        }
            //    }
            //}
            //catch (Exception e)
            //{
            //    // Do nothing
            //}
            //candles = GetCandleHistoryFromServer(startTime, endTime, security, timeFrameBuilder);


            //return candles.Count == 0 ? null : candles;
            return candles;
        }


        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            //throw new NotImplementedException();
            return null;
        }

        #endregion

        #region 6 Streams creation

        #endregion

        #region 7 Security subscribe

        public void Subscribe(Security security)
        {
            if (security == null) return;


            for (int i = 0; i < _subscribedSecurities.Count; i++)
            {
                if (_subscribedSecurities[i].security.NameClass == security.NameClass
                    && _subscribedSecurities[i].security.Name == security.Name)
                {
                    return;
                }
            }

            try
            {
                MtSecurity mtSecurity = new MtSecurity();
                //mtSecurity.NameId = security.NameId;
                //mtSecurity.NameClass = security.NameClass;
                mtSecurity.security = security;

                mtSecurity.chartId = _mtapi.ChartOpen(security.NameId, ENUM_TIMEFRAMES.PERIOD_M1);

                //if (_mtapi.MarketBookAdd(security.NameId))
                //if (mtSecurity.chartId > 0)
                //{
                mtSecurity.isMarketBookAdded = _mtapi.MarketBookAdd(security.NameId);
                if (!mtSecurity.isMarketBookAdded)
                {
                    SendLogMessage($"Security {security.Name} not subscribed. MarketBookAdd failed.", LogMessageType.Error);
                    return;
                }

                mtSecurity.isSymbolSelected = _mtapi.SymbolSelect(security.NameId, true);
                if (!mtSecurity.isSymbolSelected)
                {
                    SendLogMessage($"Security {security.Name} not subscribed. Symbol select failed.", LogMessageType.Error);
                    return;
                }

                _subscribedSecurities.Add(mtSecurity);
                //SendLogMessage($"Security {security.Name} succesfully subscribed.", LogMessageType.Error);
                //}
                //else
                //{
                //    SendLogMessage($"Security not subscribed: {security.Name}.", LogMessageType.Error);
                //}
            }
            catch (Exception ex)
            {
                SendLogMessage($"Error subscribe security {security.Name}. {ex.Message}", LogMessageType.Error);
            }
        }

        public void Unsubscribe(Security security)
        {
            if (security == null) return;

            MtSecurity mtSecurity = null;
            try
            {
                for (int i = 0; i < _subscribedSecurities.Count; i++)
                {
                    if (_subscribedSecurities[i].security.NameId == security.NameId
                        && _subscribedSecurities[i].security.NameClass == security.NameClass)
                    {
                        mtSecurity = _subscribedSecurities[i];
                        _subscribedSecurities.RemoveAt(i);
                        break;
                    }
                }

                if (mtSecurity == null) return;
                if (mtSecurity.chartId > 0)
                {
                    _mtapi.ChartClose(mtSecurity.chartId);
                }
                _mtapi.MarketBookRelease(mtSecurity.security.NameId);

            }
            catch (Exception exception)
            {
                SendLogMessage($"Unsubscribe error. {security.Name}: " + exception.ToString(), LogMessageType.Error);
            }
        }

        public bool SubscribeNews()
        {
            return false;
        }

        public event Action<News> NewsEvent;

        public List<MtSecurity> _subscribedSecurities = new List<MtSecurity>();
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

        //https://www.mql5.com/ru/docs/constants/tradingconstants/enum_trade_request_actions#trade_action_pending
        // https://www.mql5.com/ru/docs/constants/structures/mqltraderequest
        public void SendOrder(Order order)
        {
            MqlTradeRequest request = new MqlTradeRequest();
            request.Magic = (ulong)order.NumberUser;
            request.Symbol = order.SecurityNameCode;
            request.Volume = Convert.ToDouble(order.Volume);
            request.Type_time = ENUM_ORDER_TYPE_TIME.ORDER_TIME_GTC;
            //mtOrder.Comment = order.NumberUser.ToString();
            //mtOrder.Position = (ulong)order.NumberUser;
            //mtOrder.Type_filling = ENUM_ORDER_TYPE_FILLING.ORDER_FILLING_RETURN;
            if (order.TypeOrder == OrderPriceType.Limit)
            {
                request.Action = ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_PENDING;
                request.Price = Convert.ToDouble(order.Price);
                if (order.Side == Side.Buy)
                {
                    request.Type = ENUM_ORDER_TYPE.ORDER_TYPE_BUY_LIMIT;
                }

                if (order.Side == Side.Sell)
                {
                    request.Type = ENUM_ORDER_TYPE.ORDER_TYPE_SELL_LIMIT;
                }

            }
            else if (order.TypeOrder == OrderPriceType.Market)
            {
                request.Action = ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_DEAL;
                request.Type_filling = ENUM_ORDER_TYPE_FILLING.ORDER_FILLING_RETURN;

                if (order.Side == Side.Buy)
                {
                    request.Type = ENUM_ORDER_TYPE.ORDER_TYPE_BUY;
                }

                if (order.Side == Side.Sell)
                {
                    request.Type = ENUM_ORDER_TYPE.ORDER_TYPE_SELL;
                }
            }
            _mtapi.OrderSend(request, out MqlTradeResult orderState);

            if (orderState == null)
            {
                InvokeOrderFail(order);
                return;
            }

            order.State = GetRetOrderStateType(orderState.Retcode);
            order.NumberMarket = orderState.Request_id.ToString(); // Order id? TODO проверить (нужен ticket)
            order.TimeCallBack = DateTime.UtcNow.AddHours(_timezoneOffset);
            if (orderState.Price != null)
            {
                order.Price = orderState.Price.ToString().ToDecimal();
            }
            order.Volume = orderState.Volume.ToString().ToDecimal();

            if (order.State == OrderStateType.Cancel)
            {
                order.TimeCancel = DateTime.UtcNow.AddHours(_timezoneOffset);
            }

            if (order.State == OrderStateType.Done)
            {
                order.TimeDone = DateTime.UtcNow.AddHours(_timezoneOffset);
            }
            MyOrderEvent?.Invoke(order);
        }

        public OrderStateType GetOrderStatus(Order order)
        {
            ulong id = _mtapi.OrderGetTicket(Convert.ToInt32(order.NumberMarket));
            if (!_mtapi.OrderSelect(id)) return OrderStateType.None;

            order.State = GetOrderStateType(_mtapi.OrderGetInteger(ENUM_ORDER_PROPERTY_INTEGER.ORDER_STATE));

            if (order.State == OrderStateType.Done
                || order.State == OrderStateType.Partial)
            {

            }

            return order.State;
        }

        public void GetAllActivOrders()
        {
            long ordersCount = _mtapi.OrdersTotal();

            for (int i = 0; i < ordersCount; i++)
            {
                Order order = new Order();
                order.PortfolioNumber = _accountId;
                ulong ticket = _mtapi.OrderGetTicket(i);
                order = GetActiveOrderFromMt(ticket);
                if (order == null) continue;
                MyOrderEvent?.Invoke(order);
            }
        }

        public bool CancelOrder(Order order)
        {
            Order updatedOrder = CancelOrderFromMt(Convert.ToUInt32(order.NumberMarket));
            return updatedOrder.State == OrderStateType.Cancel;
        }

        public void CancelAllOrders()
        {
            //_mtapi.PositionCloseAll();
            long ordersCount = _mtapi.OrdersTotal();
            for (int i = 0; i < ordersCount; i++)
            {
                ulong ticket = _mtapi.OrderGetTicket(i);
                if (!_mtapi.OrderSelect(ticket)) continue;
                CancelOrderFromMt(ticket);
            }
        }

        // https://www.mql5.com/ru/articles/211
        // https://www.mql5.com/ru/docs/constants/tradingconstants/enum_trade_request_actions#trade_action_remove
        public void CancelAllOrdersToSecurity(Security security)
        {
            long ordersCount = _mtapi.OrdersTotal();
            for (int i = 0; i < ordersCount; i++)
            {
                ulong ticket = _mtapi.OrderGetTicket(i);
                if (!_mtapi.OrderSelect(ticket)) continue;
                string nameId = _mtapi.OrderGetString(ENUM_ORDER_PROPERTY_STRING.ORDER_SYMBOL);
                if (nameId != security.NameId) continue;
                CancelOrderFromMt(ticket);
            }
        }

        /// <summary>
        /// https://www.mql5.com/en/book/automation/experts/experts_modify_order
        /// </summary>
        /// <param name="order"></param>
        /// <param name="newPrice"></param>
        public void ChangeOrderPrice(Order order, decimal newPrice)
        {
            if (order.TypeOrder != OrderPriceType.Limit) return;
            ulong ticket = Convert.ToUInt32(order.NumberMarket);
            if (!_mtapi.OrderSelect(ticket)) return;
            MqlTradeRequest request = new MqlTradeRequest();
            request.Order = ticket;
            request.Action = ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_PENDING;
            request.Price = Convert.ToDouble(newPrice);
            //if (order.Side == Side.Buy)
            //{
            //    request.Type = ENUM_ORDER_TYPE.ORDER_TYPE_BUY_LIMIT;
            //}

            //if (order.Side == Side.Sell)
            //{
            //    request.Type = ENUM_ORDER_TYPE.ORDER_TYPE_SELL_LIMIT;
            //}

            if (!_mtapi.OrderSend(request, out MqlTradeResult? response))
            {
                SendLogMessage(string.Format("Order change price error. Code: {0}.", _mtapi.GetLastError()), LogMessageType.Error);
                return;
            }
            order.Price = newPrice;
            //order.TimeCallBack = DateTime.UtcNow.AddHours(_timezoneOffset);
            MyOrderEvent?.Invoke(order);
        }

        private Order GetActiveOrderFromMt(ulong ticket)
        {
            if (!_mtapi.OrderSelect(ticket)) return null;
            Order order = new Order();
            order.NumberMarket = ticket.ToString(); // По параметру ticket запрашивается инфо по заявке у платформы mt5
            order.SecurityNameCode = _mtapi.OrderGetString(ENUM_ORDER_PROPERTY_STRING.ORDER_SYMBOL);
            //order.NumberMarket = _mtapi.OrderGetString(ENUM_ORDER_PROPERTY_STRING.ORDER_EXTERNAL_ID);
            long pid = _mtapi.OrderGetInteger(ENUM_ORDER_PROPERTY_INTEGER.ORDER_POSITION_ID);
            long magic = _mtapi.OrderGetInteger(ENUM_ORDER_PROPERTY_INTEGER.ORDER_MAGIC);
            order.Price = Convert.ToDecimal(_mtapi.OrderGetDouble(ENUM_ORDER_PROPERTY_DOUBLE.ORDER_PRICE_OPEN));
            order.Volume = Convert.ToDecimal(_mtapi.OrderGetDouble(ENUM_ORDER_PROPERTY_DOUBLE.ORDER_VOLUME_INITIAL));
            order.State = GetOrderStateType(_mtapi.OrderGetInteger(ENUM_ORDER_PROPERTY_INTEGER.ORDER_STATE));
            order.TimeCreate = ConvertToDateTimeFromUnixFromMilliseconds(_mtapi.OrderGetInteger(ENUM_ORDER_PROPERTY_INTEGER.ORDER_TIME_SETUP_MSC));
            order.TimeCallBack = order.TimeCreate;
            long orderType = _mtapi.OrderGetInteger(ENUM_ORDER_PROPERTY_INTEGER.ORDER_TYPE);
            order.TypeOrder = GetOrderPriceType(orderType);
            order.Side = GetOrderSide(orderType);
            return order;
        }

        /// <summary>
        /// https://www.mql5.com/en/book/automation/experts/experts_remove_order
        /// </summary>
        /// <param name="ticket"></param>
        /// <returns></returns>
        private Order CancelOrderFromMt(ulong ticket)
        {
            if (!_mtapi.OrderSelect(ticket)) return null;
            MqlTradeRequest request = new MqlTradeRequest();
            Order order = GetActiveOrderFromMt(ticket);
            if (order == null) return null;
            request.Action = ENUM_TRADE_REQUEST_ACTIONS.TRADE_ACTION_REMOVE;
            request.Order = ticket;
            if (!_mtapi.OrderSend(request, out MqlTradeResult? response))
            {
                SendLogMessage(string.Format("Cancel order error. Code: {0}.", _mtapi.GetLastError()), LogMessageType.Error);
                return null;
            }
            order.State = GetRetOrderStateType(response.Retcode);
            MyOrderEvent.Invoke(order);
            return order;
        }

        //private List<Order> GetAllActiveOrdersFromExchange()
        //{
        //    long ordersCount = _mtapi.OrdersTotal();
        //    for (int i = 0; i < ordersCount; i++)
        //    {
        //        ulong ticket = _mtapi.OrderGetTicket(i);
        //        if (!_mtapi.OrderSelect(ticket)) continue;
        //        string nameId = _mtapi.OrderGetString(ENUM_ORDER_PROPERTY_STRING.ORDER_SYMBOL);
        //        if (nameId != security.NameId) continue;
        //        if (!_mtapi.PositionClose(ticket)) continue;
        //        order.State = GetOrderStateType(orderCancelResponse.Status);
        //    }
        //}

        List<Order> IServerRealization.GetActiveOrders(int startIndex, int count)
        {
            throw new NotImplementedException();
        }

        List<Order> IServerRealization.GetHistoricalOrders(int startIndex, int count)
        {
            throw new NotImplementedException();
        }

        public event Action<Order> MyOrderEvent;
        #endregion

        #region 11 Helpers
        private void InvokeOrderFail(Order order)
        {
            order.State = OrderStateType.Fail;
            MyOrderEvent?.Invoke(order);
        }

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


        /// <summary>
        ///  ORDER_STATE_STARTED = 0,            //Order checked, but not yet accepted by broker
        //ORDER_STATE_PLACED = 1,             //Order accepted
        //ORDER_STATE_CANCELED = 2,           //Order canceled by client
        //ORDER_STATE_PARTIAL = 3,            //Order partially executed
        //ORDER_STATE_FILLED = 4,             //Order fully executed
        //ORDER_STATE_REJECTED = 5,           //Order rejected
        //ORDER_STATE_EXPIRED = 6,            //Order expired
        //ORDER_STATE_REQUEST_ADD = 7,        //Order is being registered (placing to the trading system)
        //ORDER_STATE_REQUEST_MODIFY = 8,     //Order is being modified (changing its parameters)
        //ORDER_STATE_REQUEST_CANCEL = 9      //Order is being deleted (deleting from the trading system)
        /// </summary>
        /// <param name="status"></param>
        /// <returns></returns>
        private OrderStateType GetOrderStateType(long status)
        {
            return status switch
            {
                0 => OrderStateType.Pending,
                1 => OrderStateType.Active,
                2 => OrderStateType.Cancel,
                3 => OrderStateType.Partial,
                4 => OrderStateType.Done,
                5 => OrderStateType.Fail,
                6 => OrderStateType.Cancel,
                7 => OrderStateType.Pending,
                8 => OrderStateType.Pending,
                9 => OrderStateType.Pending,
                _ => OrderStateType.None
            };
        }

        // https://www.mql5.com/ru/docs/constants/errorswarnings/enum_trade_return_codes
        private OrderStateType GetRetOrderStateType(long status)
        {
            return status switch
            {
                10004 => OrderStateType.Fail, // TRADE_RETCODE_REQUOTE
                10006 => OrderStateType.Fail, // TRADE_RETCODE_REJECT
                10007 => OrderStateType.Cancel, // TRADE_RETCODE_CANCEL
                10008 => OrderStateType.Active, // TRADE_RETCODE_PLACED
                10009 => OrderStateType.Done, // TRADE_RETCODE_DONE
                10010 => OrderStateType.Partial, // TRADE_RETCODE_DONE_PARTIAL
                10011 => OrderStateType.Fail, // TRADE_RETCODE_ERROR
                10012 => OrderStateType.Cancel, // TRADE_RETCODE_TIMEOUT
                10013 => OrderStateType.Fail, // TRADE_RETCODE_INVALID
                10014 => OrderStateType.Fail, // TRADE_RETCODE_INVALID_VOLUME
                10015 => OrderStateType.Fail, // TRADE_RETCODE_INVALID_PRICE
                10016 => OrderStateType.Fail, // TRADE_RETCODE_INVALID_STOPS
                10017 => OrderStateType.Fail, // TRADE_RETCODE_TRADE_DISABLED
                10018 => OrderStateType.Fail, // TRADE_RETCODE_MARKET_CLOSED
                10019 => OrderStateType.Fail, // TRADE_RETCODE_NO_MONEY
                10020 => OrderStateType.Fail, // TRADE_RETCODE_PRICE_CHANGED
                10021 => OrderStateType.Fail, // TRADE_RETCODE_PRICE_OFF
                10022 => OrderStateType.Fail, // TRADE_RETCODE_INVALID_EXPIRATION
                10023 => OrderStateType.Fail, // TRADE_RETCODE_ORDER_CHANGED
                10024 => OrderStateType.Fail, // TRADE_RETCODE_TOO_MANY_REQUESTS
                10025 => OrderStateType.None, // TRADE_RETCODE_NO_CHANGES (Check)
                10026 => OrderStateType.Fail, // TRADE_RETCODE_SERVER_DISABLES_AT
                10027 => OrderStateType.Fail, // TRADE_RETCODE_CLIENT_DISABLES_AT
                10028 => OrderStateType.Fail, // TRADE_RETCODE_LOCKED
                10029 => OrderStateType.Fail, // TRADE_RETCODE_FROZEN
                10030 => OrderStateType.Fail, // TRADE_RETCODE_INVALID_FILL
                10031 => OrderStateType.Fail, // TRADE_RETCODE_CONNECTION
                10032 => OrderStateType.Fail, // TRADE_RETCODE_ONLY_REAL
                10033 => OrderStateType.Fail, // TRADE_RETCODE_LIMIT_ORDERS
                10034 => OrderStateType.Fail, // TRADE_RETCODE_LIMIT_VOLUME
                _ => OrderStateType.Fail
            };
        }

        /// <summary>
        /// ORDER_TYPE_BUY = 0,             //Market Buy order
        //ORDER_TYPE_SELL = 1,            //Market Sell order
        //ORDER_TYPE_BUY_LIMIT = 2,       //Buy Limit pending order
        //ORDER_TYPE_SELL_LIMIT = 3,      //Sell Limit pending order
        //ORDER_TYPE_BUY_STOP = 4,        //Buy Stop pending order
        //ORDER_TYPE_SELL_STOP = 5,       //Sell Stop pending order
        //ORDER_TYPE_BUY_STOP_LIMIT = 6,  //Upon reaching the order price, a pending Buy Limit order is places at the StopLimit price
        //ORDER_TYPE_SELL_STOP_LIMIT = 7, //Upon reaching the order price, a pending Sell Limit order is places at the StopLimit price
        //ORDER_TYPE_CLOSE_BY = 8         //Order to close a position by an opposite one
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private OrderPriceType GetOrderPriceType(long type)
        {
            return type switch
            {
                2 => OrderPriceType.Limit,
                3 => OrderPriceType.Limit,
                //6 => OrderPriceType.Limit,
                //7 => OrderPriceType.Limit,
                8 => throw new Exception("Cant get Order price type"),
                _ => OrderPriceType.Market
            };
        }

        private Side GetOrderSide(long type)
        {
            return type switch
            {
                0 => Side.Buy,
                2 => Side.Buy,
                4 => Side.Buy,
                6 => Side.Buy,
                1 => Side.Sell,
                3 => Side.Sell,
                5 => Side.Sell,
                7 => Side.Sell,
                8 => throw new Exception("Cant get Order side"),
                _ => Side.None
            };
        }

        //public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        //{
        //    // Unix timestamp is seconds past epoch
        //    DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        //    dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
        //    return dateTime;
        //}

        private DateTime ConvertToDateTimeFromUnixFromMilliseconds(long milliseconds)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            DateTime result = origin.AddMilliseconds(milliseconds).AddHours(3); // force to Moscow time zone gmt+3

            return result;
        }

        //private Security GetSecurity(string symbol)
        //{
        //    if (_securities == null) return null;
        //    for (int i = 0; i < _securities.Count; i++)
        //    {
        //        if (_securities[i].NameId == symbol)
        //        {
        //            return _securities[i];
        //        }
        //    }

        //    return null;
        //}

        //private FTimeFrame CreateTimeFrameInterval(TimeFrame tf)
        //{
        //    return tf switch
        //    {
        //        TimeFrame.Min1 => FTimeFrame.M1,
        //        TimeFrame.Min5 => FTimeFrame.M5,
        //        TimeFrame.Min15 => FTimeFrame.M15,
        //        TimeFrame.Min30 => FTimeFrame.M30,
        //        TimeFrame.Hour1 => FTimeFrame.H1,
        //        TimeFrame.Hour2 => FTimeFrame.H2,
        //        TimeFrame.Hour4 => FTimeFrame.H4,
        //        TimeFrame.Day => FTimeFrame.D,
        //        _ => FTimeFrame.Unspecified
        //    };
        //}

        private ENUM_TIMEFRAMES GetMtTimeFrame(TimeFrame tf)
        {
            return tf switch
            {
                TimeFrame.Min1 => ENUM_TIMEFRAMES.PERIOD_M1,
                TimeFrame.Min2 => ENUM_TIMEFRAMES.PERIOD_M2,
                TimeFrame.Min3 => ENUM_TIMEFRAMES.PERIOD_M3,
                TimeFrame.Min5 => ENUM_TIMEFRAMES.PERIOD_M5,
                TimeFrame.Min10 => ENUM_TIMEFRAMES.PERIOD_M10,
                TimeFrame.Min15 => ENUM_TIMEFRAMES.PERIOD_M15,
                TimeFrame.Min20 => ENUM_TIMEFRAMES.PERIOD_M20,
                TimeFrame.Min30 => ENUM_TIMEFRAMES.PERIOD_M30,
                TimeFrame.Hour1 => ENUM_TIMEFRAMES.PERIOD_H1,
                TimeFrame.Hour2 => ENUM_TIMEFRAMES.PERIOD_H2,
                TimeFrame.Hour4 => ENUM_TIMEFRAMES.PERIOD_H4,
                TimeFrame.Day => ENUM_TIMEFRAMES.PERIOD_D1,
                _ => ENUM_TIMEFRAMES.PERIOD_CURRENT
            };
        }

        private Side GetSide(ENUM_ORDER_TYPE side)
        {
            return side switch
            {
                ENUM_ORDER_TYPE.ORDER_TYPE_BUY => Side.Buy,
                ENUM_ORDER_TYPE.ORDER_TYPE_BUY_LIMIT => Side.Buy,
                ENUM_ORDER_TYPE.ORDER_TYPE_BUY_STOP => Side.Buy,
                ENUM_ORDER_TYPE.ORDER_TYPE_BUY_STOP_LIMIT => Side.Buy,
                ENUM_ORDER_TYPE.ORDER_TYPE_SELL => Side.Sell,
                ENUM_ORDER_TYPE.ORDER_TYPE_SELL_LIMIT => Side.Sell,
                ENUM_ORDER_TYPE.ORDER_TYPE_SELL_STOP => Side.Sell,
                ENUM_ORDER_TYPE.ORDER_TYPE_SELL_STOP_LIMIT => Side.Sell,
                _ => throw new Exception("Order side is not defined!")
            };
        }

        private int GetDecimals(decimal x)
        {
            var precision = 0;
            while (x * (decimal)Math.Pow(10, precision) != Math.Round(x * (decimal)Math.Pow(10, precision)))
                precision++;
            return precision;
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

        #region 13 Structures
        public class MtSecurity
        {
            public long chartId = 0;
            public bool isMarketBookAdded = false;
            public bool isSymbolSelected = false;
            public Security security = null;
        }
        #endregion
    }
}