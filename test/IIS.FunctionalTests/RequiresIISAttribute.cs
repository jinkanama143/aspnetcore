// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.AspNetCore.Testing.xunit;
using Microsoft.Win32;

namespace Microsoft.AspNetCore.Server.IISIntegration.FunctionalTests
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequiresIISAttribute : Attribute, ITestCondition
    {
        private static readonly bool _isMetStatic;
        private static readonly string _skipReasonStatic;

        private readonly bool _isMet;
        private readonly string _skipReason;

        private static readonly bool _websocketsAvailable;
        private static readonly bool _windowsAuthAvailable;
        private static readonly bool _poolEnvironmentVariablesAvailable;

        static RequiresIISAttribute()
        {
            if (Environment.GetEnvironmentVariable("ASPNETCORE_TEST_SKIP_IIS") == "true")
            {
                _skipReasonStatic = "Test skipped using ASPNETCORE_TEST_SKIP_IIS environment variable";
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _skipReasonStatic = "IIS tests can only be run on Windows";
                return;
            }

            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                _skipReasonStatic += "The current console is not running as admin.";
                return;
            }

            if (!File.Exists(Path.Combine(Environment.SystemDirectory, "inetsrv", "w3wp.exe")))
            {
                _skipReasonStatic += "The machine does not have IIS installed.";
                return;
            }

            var ancmConfigPath = Path.Combine(Environment.SystemDirectory, "inetsrv", "config", "schema", "aspnetcore_schema_v2.xml");

            if (!File.Exists(ancmConfigPath))
            {
                _skipReasonStatic = "IIS Schema is not installed.";
                return;
            }

            XDocument ancmConfig;

            try
            {
                ancmConfig = XDocument.Load(ancmConfigPath);
            }
            catch
            {
                _skipReasonStatic = "Could not read ANCM schema configuration";
                return;
            }

            _isMetStatic = ancmConfig
                .Root
                .Descendants("attribute")
                .Any(n => "hostingModel".Equals(n.Attribute("name")?.Value, StringComparison.Ordinal));

            _skipReasonStatic = _isMetStatic ? null : "IIS schema needs to be upgraded to support ANCM.";

            _websocketsAvailable = File.Exists(Path.Combine(Environment.SystemDirectory, "inetsrv", "iiswsock.dll"));

            _windowsAuthAvailable = File.Exists(Path.Combine(Environment.SystemDirectory, "inetsrv", "authsspi.dll"));

            var iisRegistryKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\InetStp", writable: false);
            if (iisRegistryKey == null)
            {
                _poolEnvironmentVariablesAvailable = false;
            }
            else
            {
                var majorVersion = (int)iisRegistryKey.GetValue("MajorVersion", -1);
                var minorVersion = (int)iisRegistryKey.GetValue("MinorVersion", -1);
                var version = new Version(majorVersion, minorVersion);
                _poolEnvironmentVariablesAvailable = version >= new Version(10, 0);
            }
        }

        public RequiresIISAttribute() 
            : this (IISCapability.None) { }

        public RequiresIISAttribute(IISCapability capabilities)
        {
            _isMet = _isMetStatic;
            _skipReason = _skipReasonStatic;
            if (capabilities.HasFlag(IISCapability.Websockets))
            {
                _isMet &= _websocketsAvailable;
                if (!_websocketsAvailable)
                {
                    _skipReason += "The machine does not have IIS websockets installed.";
                }
            }
            if (capabilities.HasFlag(IISCapability.WindowsAuthentication))
            {
                _isMet &= _windowsAuthAvailable;

                if (!_windowsAuthAvailable)
                {
                    _skipReason += "The machine does not have IIS windows authentication installed.";
                }
            }
            if (capabilities.HasFlag(IISCapability.PoolEnvironmentVariables))
            {
                _isMet &= _poolEnvironmentVariablesAvailable;
                if (!_poolEnvironmentVariablesAvailable)
                {
                    _skipReason += "The machine does allow for setting environment variables on application pools.";
                }
            }

            if (capabilities.HasFlag(IISCapability.ShutdownToken))
            {
                _isMet = false;
                _skipReason += "https://github.com/aspnet/IISIntegration/issues/1074";
            }
        }

        public bool IsMet => _isMet;
        public string SkipReason => _skipReason;
    }
}
