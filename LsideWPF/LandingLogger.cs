﻿using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using CsvHelper.TypeConversion;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;

namespace LsideWPF
{
    public class LandingLogger
    {
        public class LogEntry
        {
            [Name("Time")]
            public DateTime Time { get; set; }
            [Name("Plane")]
            public string Plane { get; set; }
            [Name("FPM")]
            public int Fpm { get; set; }
            [Name("Slowing Distance (ft)")]
            public int SlowingDistance { get; set; }
            [Name("Impact (G)")]
            public double Gforce { get; set; }
            [Name("Air Speed (kt)")]
            public double AirSpeedInd { get; set; }
            [Name("Ground Speed (kt)")]
            public double GroundSpeed { get; set; }
            [Name("Headwind (kt)")]
            public double HeadWind { get; set; }
            [Name("Crosswind (kt)")]
            public double CrossWind { get; set; }
            [Name("SlipAngle (deg)")]
            public double SlipAngle { get; set; }
            [Name("Bounces")]
            public int Bounces { get; set; }
            [Name("Bank Angle (deg)")]
            public double BankAngle { get; set; }
            [Name("Distance From Aim Point (ft)")]
            public int AimPointOffset { get; set; }
            [Name("Offser From Cnt Line (ft)")]
            public int CntLineOffser { get; set; }
            [Name("Airport")]
            public string Airport { get; set; }
            [Name("DriftAngle (deg)")]
            public double DriftAngle { get; set; }
        }

        public static string GetPath()
        {
            string myDocs = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string dir = Properties.Settings.Default.LandingDirectory;
            string filename = Properties.Settings.Default.LandingFile;
            Directory.CreateDirectory(myDocs + "\\" + dir); //create if doesn't exist
            string path = myDocs + "\\" + dir + "\\" + filename;

            return path;
        }

        public string MakeLogIfEmpty()
        {
            //const string header = "Time,Plane,FPM,Impact (G),Air Speed (kt),Ground Speed (kt),Headwind (kt),Crosswind (kt),Sideslip (deg)";

            string path = GetPath();
            if (!File.Exists(path))
            {
                try
                {
                    using (var writer = new StreamWriter(path))
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteRecords(new List<LogEntry>());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"While creating Landing Logger file {path}",ex);
                }
            }
            return path;
        }

        public void EnterLog(LogEntry newLine)
        {
            CsvConfiguration config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                // Don't write the header again.
                HasHeaderRecord = false,
            };

            var options = new TypeConverterOptions { Formats = new[] { "dd/MM/yyyy HH:mm" } };

            string path = MakeLogIfEmpty();

            // Append to the file.
            using (var stream = File.Open(path, FileMode.Append))
            using (var writer = new StreamWriter(stream))
            using (var csv = new CsvWriter(writer, config))
            {
                csv.Context.TypeConverterOptionsCache.AddOptions<DateTime>(options);
                List<LogEntry> newRecord = new List<LogEntry>();
                newRecord.Add(newLine);
                csv.WriteRecords(newRecord);
            }
        }

        // Load the Landing log from file
        public DataTable LandingLog
        {
            get
            {
                MakeLogIfEmpty();
                return GetLandingLogData();
            }
        }

        public static DataTable GetLandingLogData()
        {
            var dt = new DataTable();
            string path = GetPath();
            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                // Do any configuration to `CsvReader` before creating CsvDataReader.
                using (var dr = new CsvDataReader(csv))
                {
                    dt.Load(dr);
                }
            }
            dt.DefaultView.Sort = "Time desc";
            dt = dt.DefaultView.ToTable();

            DataTable clone = dt.Clone();

            for (int i = 2; i < clone.Columns.Count; i++)
            {
                clone.Columns[i].DataType = dt.Columns[i].DataType;
            }

            foreach (DataRow row in dt.Rows)
            {
                clone.ImportRow(row);
            }

            return clone;
        }
    }
}
