﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Zetetic.Ldap
{
    public delegate void EndEntryEventHandler(object sender, DnEventArgs e);
    public delegate void BeginEntryEventHandler(object sender, DnEventArgs e);
    public delegate void AttributeEventHandler(object sender, AttributeEventArgs e);

    /// <summary>
    /// LdifReader raises DnEventArgs when encountering a new entry or closing out an existing one
    /// </summary>
    public class DnEventArgs : EventArgs
    {
        public string DistinguishedName { get; protected set; }

        public DnEventArgs(string dn)
        {
            this.DistinguishedName = dn;
        }
    }

    /// <summary>
    /// LdifReader raises AttributeEventArgs upon reading a complete single attribute value
    /// (which may span multiple folded lines)
    /// </summary>
    public class AttributeEventArgs : EventArgs
    {
        public string Name { get; protected set; }
        public object Value { get; protected set; }

        public AttributeEventArgs(string attrName, object attrValue)
        {
            this.Name = attrName;
            this.Value = attrValue;
        }
    }

    /// <summary>
    /// LdifReader is a low-overhead, event-based reader for LDIF formatted files.  Use LdifEntryReader
    /// for a more traditional, "whole-entry" reader.
    /// </summary>
    public class LdifReader : IDisposable
    {
        private TextReader _source;
        private bool _ownsStream;
        private bool _openEntry;

        public string LastDn { get; protected set; }

        /// <summary>
        /// Open the ASCII-encoded LDIF file at 'path' for reading.
        /// </summary>
        /// <param name="path"></param>
        public LdifReader(string path) 
        {
            _source = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read), Encoding.ASCII);
            _ownsStream = true;
        }

        /// <summary>
        /// Read from an already-open TextReader, without assuming any particular encoding.
        /// </summary>
        /// <param name="sr"></param>
        public LdifReader(TextReader sr)
        {
            _source = sr;
            _ownsStream = false;
        }

        /// <summary>
        /// Read from an already-open FileStream, assumed to be ASCII-encoded.
        /// </summary>
        /// <param name="fs"></param>
        public LdifReader(FileStream fs)
        {
            _source = new StreamReader(fs, Encoding.ASCII);
            _ownsStream = false;
        }

        public event EndEntryEventHandler OnEndEntry;
        public event BeginEntryEventHandler OnBeginEntry;
        public event AttributeEventHandler OnAttributeValue;

        /// <summary>
        /// Raise any events from reading the next set of instructions from the LDIF.
        /// Return false if we have reached the end of the file.  Otherwise, true.
        /// </summary>
        /// <returns>True if there is still more to read, false if EOF</returns>
        public bool Read()
        {
            string line = _source.ReadLine();

            if (line != null && line.StartsWith("#"))
            {
                // Skip comment line
                return true;
            }

            if (string.IsNullOrEmpty(line))
            {
                if (_openEntry)
                {
                    _openEntry = false;

                    if (OnEndEntry != null)
                        OnEndEntry(this, new DnEventArgs(this.LastDn));
                }

                // End of file
                return (line != null);
            }

            if (line.StartsWith("dn:"))
            {
                _openEntry = true;

                bool b64 = (line[3] == ':');
                string dn = line.Substring(b64 ? 4 : 3).TrimStart();

                while (_source.Peek() == (int)' ')
                    dn += _source.ReadLine().Substring(1);

                if (b64) dn = Encoding.UTF8.GetString(Convert.FromBase64String(dn));
                this.LastDn = dn;

                if (OnBeginEntry != null)
                    OnBeginEntry(this, new DnEventArgs(dn));
                
            }
            else
            {
                int fc = line.IndexOf(':');
                string attrName = line.Substring(0, fc);

                bool b64 = (line[fc + 1] == ':');

                string attrVal = line.Substring(b64 ? fc +2 : fc + 1).TrimStart();
                
                while (_source.Peek() == (int)' ')
                    attrVal += _source.ReadLine().Substring(1);

                if (OnAttributeValue != null)
                {
                    if (b64)
                        OnAttributeValue(this, new AttributeEventArgs(attrName, Convert.FromBase64String(attrVal)));
                    else
                        OnAttributeValue(this, new AttributeEventArgs(attrName, attrVal));
                }
            }

            return true;
        }

        /// <summary>
        /// Close the underlying stream if we own it; otherwise, just nullify our reference.
        /// </summary>
        /// <param name="isDisposing"></param>
        public void Dispose(bool isDisposing)
        {
            if (_source != null)
            {
                if (_ownsStream)
                    _source.Dispose();

                _source = null;
            }

            if (isDisposing)
                System.GC.SuppressFinalize(this);
        }

        #region IDisposable Members

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

        ~LdifReader()
        {
            Dispose(false);
        }
    }
}
