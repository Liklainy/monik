﻿using Gerakul.FastSql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Monik;
using Monik.Common;
using System.Diagnostics;
using Monik.Client;
using System.Threading;

namespace Monik.Service
{
  public class MessageProcessor : IMessageProcessor
  {
    private IRepository FRepository;
    private ICacheLog FCacheLog;
    private ICacheKeepAlive FCacheKeepAlive;

    public MessageProcessor(IRepository aRepository, ICacheLog aCacheLog, ICacheKeepAlive aCacheKeepAlive)
    {
      FRepository = aRepository;
      FCacheLog = aCacheLog;
      FCacheKeepAlive = aCacheKeepAlive;
      FCleaner = Scheduler.CreatePerHour(CleanerTask, "cleaner");
      FStatist = Scheduler.CreatePerHour(StatistTask, "statist");
      M.ApplicationInfo("MessageProcessor created");
    }

    private Scheduler FCleaner;
    private Scheduler FStatist;

    public void OnStart()
    {
      FCleaner.OnStart();
      FStatist.OnStart();
    }

    private void CleanerTask()
    {
      try
      {
        // cleanup logs
        var _logDeep = int.Parse(Settings.GetValue("DayDeepLog"));
        var _logThreshold = FRepository.GetLogThreshold(_logDeep);
        if (_logThreshold.HasValue)
        {
          var _count = FRepository.CleanUpLog(_logThreshold.Value);
          M.LogicInfo("CleanerTask delete Log: {0} rows", _count);
        }

        // cleanup keep-alive
        var _kaDeep = int.Parse(Settings.GetValue("DayDeepKeepAlive"));
        var _kaThreshold = FRepository.GetKeepAliveThreshold(_kaDeep);
        if (_kaThreshold.HasValue)
        {
          var _count = FRepository.CleanUpKeepAlive(_kaThreshold.Value);
          M.LogicInfo("CleanerTask delete KeepAlive: {0} rows", _count);
        }
      }
      catch (Exception _e)
      {
        M.ApplicationError("CleanerTask: {0}", _e.Message);
      }
    }

    private void StatistTask()
    {
      try
      {
        DateTime now = DateTime.UtcNow;
        DateTime hs = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

        FRepository.CreateHourStat(hs, FCacheLog.LastLogID, FCacheKeepAlive.LastKeepAliveID);
      }
      catch (Exception _e)
      {
        M.ApplicationError("StatistTask: {0}", _e.Message);
      }
    }

    public void OnStop()
    {
      FStatist.OnStop();
      FCleaner.OnStop();
    }

    public void Process(Event aEvent, Instance aInstance)
    {
      switch (aEvent.MsgCase)
      {
        case Event.MsgOneofCase.None:
          throw new NotSupportedException("Bad event type");
        case Event.MsgOneofCase.Ka:
          var _ka = WriteKeepAlive(aEvent, aInstance);
          FCacheKeepAlive.OnNewKeepAlive(_ka);
          break;
        case Event.MsgOneofCase.Lg:
          var _lg = WriteLog(aEvent, aInstance);
          FCacheLog.OnNewLog(_lg);
          break;
        default:
          throw new NotSupportedException("Bad event type");
      }
    }

    private KeepAlive_ WriteKeepAlive(Event aEventLog, Instance aInstance)
    {
      KeepAlive_ _row = new KeepAlive_()
      {
        Created = Helper.FromMillisecondsSinceUnixEpoch(aEventLog.Created),
        Received = DateTime.UtcNow,
        InstanceID = aInstance.ID
      };

      FRepository.CreateKeepAlive(_row);

      return _row;
    }

    private Log_ WriteLog(Event aEventLog, Instance aInstance)
    {
      Log_ _row = new Log_()
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

      FRepository.CreateLog(_row);

      return _row;
    }
  }
}
