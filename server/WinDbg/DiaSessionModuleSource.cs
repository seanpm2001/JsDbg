﻿using System;
using System.Threading.Tasks;
using Microsoft.Debuggers.DbgEng;
using Dia2Lib;
using JsDbg.WinDbg;

namespace JsDbg.Dia.WinDbg {
    class DiaSessionModuleSource : IDiaSessionSource {
        private class ModuleReader : IDiaReadExeAtRVACallback, IDiaLoadCallback {
            internal ModuleReader(SymbolCache symbolCache, DebugDataSpaces dataSpaces, string module) {
                this.dataSpaces = dataSpaces;
                this.baseAddress = symbolCache.GetModuleBase(module);
            }

            private DebugDataSpaces dataSpaces;
            private ulong baseAddress;

            #region IDiaReadExeAtRVACallback Members

            public void ReadExecutableAtRVA(uint relativeVirtualAddress, uint cbData, ref uint pcbData, byte[] data) {
                pcbData = this.dataSpaces.ReadVirtual<byte>(this.baseAddress + relativeVirtualAddress, data);
            }

            #endregion

            #region IDiaLoadCallback Members

            public void NotifyDebugDir(bool fExecutable, uint cbData, byte[] data) { }

            public void NotifyOpenDBG(string dbgPath, uint resultCode) { }

            public void NotifyOpenPDB(string pdbPath, uint resultCode) {
                Console.WriteLine("Attempting to read PDB: {0}", pdbPath);
            }

            public void RestrictRegistryAccess() {
                return; // Allow it.
            }

            public void RestrictSymbolServerAccess() {
                return; // Allow it.
            }

            #endregion
        }

        internal DiaSessionModuleSource(WinDbgDebuggerRunner runner, SymbolCache symbolCache, DebugDataSpaces dataSpaces) {
            this.runner = runner;
            this.symbolCache = symbolCache;
            this.dataSpaces = dataSpaces;
        }

        private string SymPath {
            get {
                string[] caches = { "", @"C:\symbols", @"C:\debuggers\wow64\sym", @"C:\debuggers\sym" };
                string[] servers = { "", "http://symweb/" };
                return String.Format("CACHE*{0};{2};SRV*{1}", string.Join(";CACHE*", caches), string.Join(";SRV*", servers), this.symbolCache.GetSymbolSearchPath());
            }
        }

        #region IDiaSessionSource Members

        public Task WaitUntilReady() {
            return this.runner.WaitForBreakIn();
        }

        public IDiaSession LoadSessionForModule(string moduleName) {
            DiaSource source = new DiaSource();
            try {
                source.loadDataForExe(moduleName, this.SymPath, new ModuleReader(this.symbolCache, this.dataSpaces, moduleName));
            } catch (InvalidOperationException) {
                throw new DiaSourceNotReadyException();
            }
            IDiaSession session;
            source.openSession(out session);
            return session;
        }

        #endregion

        private WinDbgDebuggerRunner runner;
        private SymbolCache symbolCache;
        private DebugDataSpaces dataSpaces;
    }
}