﻿using System;
using Monik.Common;

namespace Monik.Service
{
    public class MessageProcessor : IMessageProcessor
    {
        private readonly IMonikServiceSettings _settings;
        private readonly IRepository _repository;
        private readonly ICacheLog _cacheLog;
        private readonly ICacheKeepAlive _cacheKeepAlive;
        private readonly IMonik _monik;

        public MessageProcessor(IMonikServiceSettings settings, IRepository repository, 
            ICacheLog cacheLog, ICacheKeepAlive cacheKeepAlive, IMonik monik)
        {
            _settings = settings;
            _repository = repository;
            _cacheLog = cacheLog;
            _cacheKeepAlive = cacheKeepAlive;
            _monik = monik;

            _cleaner = Scheduler.CreatePerHour(_monik, CleanerTask, "cleaner");
            _statist = Scheduler.CreatePerHour(_monik, StatistTask, "statist");

            _monik.ApplicationVerbose("MessageProcessor created");
        }

        private readonly Scheduler _cleaner;
        private readonly Scheduler _statist;

        public void OnStart()
        {
            _cleaner.OnStart();
            _statist.OnStart();

            _monik.ApplicationVerbose("MessageProcessor started");
        }

        private void CleanerTask()
        {
            try
            {
                // cleanup logs
                var logDeep = _settings.DayDeepLog;
                var logThreshold = _repository.GetLogThreshold(logDeep);
                if (logThreshold.HasValue)
                {
                    var count = _repository.CleanUpLog(logThreshold.Value);
                    _monik.LogicInfo("Cleaner delete Log: {0} rows", count);
                }

                // cleanup keep-alive
                var kaDeep = _settings.DayDeepKeepAlive;
                var kaThreshold = _repository.GetKeepAliveThreshold(kaDeep);
                if (kaThreshold.HasValue)
                {
                    var count = _repository.CleanUpKeepAlive(kaThreshold.Value);
                    _monik.LogicInfo("Cleaner delete KeepAlive: {0} rows", count);
                }
            }
            catch (Exception ex)
            {
                _monik.ApplicationError("CleanerTask: {0}", ex.Message);
            }
        }

        private void StatistTask()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                DateTime hs = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

                _repository.CreateHourStat(hs, _cacheLog.LastLogId, _cacheKeepAlive.LastKeepAliveId);
            }
            catch (Exception ex)
            {
                _monik.ApplicationError("StatistTask: {0}", ex.Message);
            }
        }

        public void OnStop()
        {
            _statist.OnStop();
            _cleaner.OnStop();
        }

        public void Process(Event aEvent, Instance aInstance)
        {
            switch (aEvent.MsgCase)
            {
                case Event.MsgOneofCase.None:
                    throw new NotSupportedException("Bad event type");
                case Event.MsgOneofCase.Ka:
                    var ka = WriteKeepAlive(aEvent, aInstance);
                    _cacheKeepAlive.OnNewKeepAlive(ka);
                    break;
                case Event.MsgOneofCase.Lg:
                    var lg = WriteLog(aEvent, aInstance);
                    _cacheLog.OnNewLog(lg);
                    break;
                default:
                    throw new NotSupportedException("Bad event type");
            }
        }

        private KeepAlive_ WriteKeepAlive(Event aEventLog, Instance aInstance)
        {
            KeepAlive_ row = new KeepAlive_()
            {
                Created = Helper.FromMillisecondsSinceUnixEpoch(aEventLog.Created),
                Received = DateTime.UtcNow,
                InstanceID = aInstance.ID
            };

            _repository.CreateKeepAlive(row);

            return row;
        }

        private Log_ WriteLog(Event aEventLog, Instance aInstance)
        {
            Log_ row = new Log_()
            {
                Created = Helper.FromMillisecondsSinceUnixEpoch(aEventLog.Created),
                Received = DateTime.UtcNow,
                Level = (byte)aEventLog.Lg.Level,
                Severity = (byte)aEventLog.Lg.Severity,
                InstanceID = aInstance.ID,
                Format = (byte)aEventLog.Lg.Format,
                Body = aEventLog.Lg.Body,
                Tags = aEventLog.Lg.Tags
            };

            _repository.CreateLog(row);

            return row;
        }
    }//end of class
}
