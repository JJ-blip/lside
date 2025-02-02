﻿namespace LsideWPF.Services
{
    using System.Collections.Generic;
    using System.Data;
    using LsideWPF.Common;

    public interface ISlipLogger
    {
        // Only meaningfull after FinishLogging completion
        List<SlipLogEntry> GetLogEntries();

        // Only meaningfull after FinishLogging completion
        string GetFullFilename();

        bool HasCompleted();

        bool IsArmed();

        void BeginLogging();

        void Log(PlaneInfoResponse response);

        void FinishLogging();

        void CancelLogging();

        List<SlipLogEntry> GetListDataTable(DataTable dt);

        double GetAverageCrosswind(long requiredId);

        double GetAverageHeadwind(long requiredId);
    }
}
