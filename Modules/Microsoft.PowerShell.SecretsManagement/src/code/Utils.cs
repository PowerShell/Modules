// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Remoting.Internal;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;

using Dbg = System.Diagnostics.Debug;

namespace Microsoft.PowerShell.SecretsManagement
{
#if !UNIX
    #region CredMan

    /// <summary>
    /// Windows Credential Manager (CredMan) native method PInvokes.
    /// </summary>
    internal static class NativeUtils
    {
        #region Constants

        /// <summary>
        /// CREDENTIAL Flags
        /// </summary>
        public enum CRED_FLAGS
        {
            PROMPT_NOW = 2,
            USERNAME_TARGET = 4
        }

        /// <summary>
        /// CREDENTIAL Types
        /// </summary>
        public enum CRED_TYPE
        {
            GENERIC = 1,
            DOMAIN_PASSWORD = 2,
            DOMAIN_CERTIFICATE = 3,
            DOMAIN_VISIBLE_PASSWORD = 4,
            GENERIC_CERTIFICATE = 5,
            DOMAIN_EXTENDED = 6,
            MAXIMUM = 7
        }

        /// <summary>
        /// Credential Persist
        /// </summary>
        public enum CRED_PERSIST 
        {
            SESSION = 1,
            LOCAL_MACHINE = 2,
            ENTERPRISE = 3
        }

        // Credential Read/Write GetLastError errors (winerror.h)
        public const uint PS_ERROR_BUFFER_TOO_LARGE = 1783;         // Error code 1783 seems to appear for too large buffer (2560 string characters)
        public const uint ERROR_NO_SUCH_LOGON_SESSION = 1312;
        public const uint ERROR_INVALID_PARAMETER = 87;
        public const uint ERROR_INVALID_FLAGS = 1004;
        public const uint ERROR_BAD_USERNAME = 2202;
        public const uint ERROR_NOT_FOUND = 1168;
        public const uint SCARD_E_NO_READERS_AVAILABLE = 0x8010002E;
        public const uint SCARD_E_NO_SMARTCARD = 0x8010000C;
        public const uint SCARD_W_REMOVED_CARD = 0x80100069;
        public const uint SCARD_W_WRONG_CHV = 0x8010006B;

        #endregion

        #region Data structures

        [StructLayout(LayoutKind.Sequential)]
        public class CREDENTIALA
        {
            /// <summary>
            /// Specifies characteristics of the credential.
            /// </summary>
            public uint Flags;

            /// <summary>
            /// Type of Credential.
            /// </summary>
            public uint Type;

            /// <summary>
            /// Name of the credential.
            /// </summary>
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            public string TargetName;

            /// <summary>
            /// Comment string.
            /// </summary>
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            public string Comment;

            /// <summary>
            /// Last modification of credential.
            /// </summary>
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;

            /// <summary>
            /// Size of credential blob in bytes.
            /// </summary>
            public uint CredentialBlobSize;

            /// <summary>
            /// Secret data for credential.
            /// </summary>
            public IntPtr CredentialBlob;

            /// <summary>
            /// Defines persistence of credential.
            /// </summary>
            public uint Persist;

            /// <summary>
            /// Number of attributes associated with this credential.
            /// </summary>
            public uint AttributeCount;

            /// <summary>
            /// Application defined attributes for credential.
            /// </summary>
            public IntPtr Attributes;

            /// <summary>
            /// Alias for the target name (max size 256 characters).
            /// </summary>
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            public string TargetAlias;

            /// <summary>
            /// User name of account for TargetName (max size 513 characters).
            /// </summary>
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            public string UserName;
        }

        #endregion

        #region Methods

        [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredWriteW(
            IntPtr Credential,
            uint Flags);

        [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredReadW(
            [InAttribute()]
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            string TargetName,
            int Type,
            int Flags,
            out IntPtr Credential);

        [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredDeleteW(
            [InAttribute()]
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            string TargetName,
            int Type,
            int Flags);

        [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredEnumerateW(
            [InAttribute()]
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            string Filter,
            int Flags,
            out int Count,
            out IntPtr Credentials);

        [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredFree(
            IntPtr Buffer);

        #endregion
    }

    /// <summary>
    /// Default local secret store
    /// </summary>
    internal static class LocalSecretStore
    {
        #region Members

        private const string PSTag = "__PS_";
        private const string PSHashtableTag = "__PSHT_";
        private const string ByteArrayType = "ByteArrayType";
        private const string StringType = "StringType";
        private const string SecureStringType = "SecureStringType";
        private const string PSCredentialType = "CredentialType";
        private const string HashtableType = "HashtableType";

        private const int MaxHashtableItemCount = 20;

        #endregion

        #region Public methods

        //
        // Vault methods currently only support the following types
        //
        // byte[] (blob)
        // string
        // SecureString
        // PSCredential
        // Hashtable
        //   Dictionary<string, object>
        //   ,where object type is: byte[], string, SecureString, Credential
        //

        /// <summary>
        /// Writes an object to the local secret vault for the current logged on user.
        /// </summary>
        /// <param name="name">Name of object to write.</param>
        /// <param name="objectToWrite">Object to write to vault.</param>
        /// <param name="errorCode">Error code or zero.</param>
        /// <returns>True on successful write.</returns>
        public static bool WriteObject<T>(
            string name,
            T objectToWrite,
            ref int errorCode)
        {
            return WriteObjectImpl(
                PrependTag(name),
                objectToWrite,
                ref errorCode);
        }

        private static bool WriteObjectImpl<T>(
            string name,
            T objectToWrite,
            ref int errorCode)
        {
            switch (objectToWrite)
            {
                case byte[] blobToWrite:
                    return WriteBlob(
                        name,
                        blobToWrite,
                        ByteArrayType,
                        ref errorCode);

                case string stringToWrite:
                    return WriteString(
                        name,
                        stringToWrite,
                        ref errorCode);

                case SecureString secureStringToWrite:
                    return WriteSecureString(
                        name,
                        secureStringToWrite,
                        ref errorCode);

                case PSCredential credentialToWrite:
                    return WritePSCredential(
                        name,
                        credentialToWrite,
                        ref errorCode);

                case Hashtable hashtableToWrite:
                    return WriteHashtable(
                        name,
                        hashtableToWrite,
                        ref errorCode);
                
                default:
                    throw new InvalidOperationException("Invalid type. Types supported: byte[], string, SecureString, PSCredential, Hashtable");
            }
        }

        /// <summary>
        /// Reads an object from the local secret vault for the current logged on user.
        /// </summary>
        /// <param name="name">Name of object to read from vault.</param>
        /// <param name="outObject">Object read from vault.</param>
        /// <param name="errorCode">Error code or zero.</param>
        /// <returns>True on successful read.</returns>
        public static bool ReadObject(
            string name,
            out object outObject,
            ref int errorCode)
        {
            return ReadObjectImpl(
                PrependTag(name),
                out outObject,
                ref errorCode);
        }

        private static bool ReadObjectImpl(
            string name,
            out object outObject,
            ref int errorCode)
        {
            if (!ReadBlob(
                name,
                out byte[] outBlob,
                out string typeName,
                ref errorCode))
            {
                outObject = null;
                return false;
            }

            switch (typeName)
            {
                case ByteArrayType:
                    outObject = outBlob;
                    return true;

                case StringType:
                    return ReadString(
                        outBlob,
                        out outObject);

                case SecureStringType:
                    return ReadSecureString(
                        outBlob,
                        out outObject);

                case PSCredentialType:
                    return ReadPSCredential(
                        outBlob,
                        out outObject);
                
                case HashtableType:
                    return ReadHashtable(
                        name,
                        outBlob,
                        out outObject,
                        ref errorCode);

                default:
                    throw new InvalidOperationException("Invalid type. Types supported: byte[], string, SecureString, PSCredential, Hashtable");
            }
        }

        /// <summary>
        /// Enumerate objects in the vault, based on the filter string, for the current user.
        /// <summary>
        /// <param name="filter">String with '*' wildcard that determins which objects to return.</param>
        /// <param name="all">Lists all objects in store without the prepended tag.</param>
        /// <param name="outObjects">Array of key/value pairs for each returned object.</param>
        /// <param name="errorCode">Error code or zero.</param>
        /// <returns>True when objects are found and returned.</returns>
        public static bool EnumerateObjects(
            string filter,
            bool all,
            out KeyValuePair<string, object>[] outObjects,
            ref int errorCode)
        {
            if (!all)
            {
                filter = PrependTag(filter);
            }

            if (!EnumerateBlobs(
                filter,
                out EnumeratedBlob[] outBlobs,
                ref errorCode))
            {
                outObjects = null;
                return false;
            }

            var outList = new List<KeyValuePair<string, object>>(outBlobs.Length);
            foreach (var item in outBlobs)
            {
                switch (item.TypeName)
                {
                    case ByteArrayType:
                        outList.Add(
                            new KeyValuePair<string, object>(
                                RemoveTag(item.Name),
                                item.Data));
                        break;

                    case StringType:
                        outList.Add(
                            new KeyValuePair<string, object>(
                                RemoveTag(item.Name),
                                Encoding.UTF8.GetString(item.Data)));
                        break;

                    case SecureStringType:
                        if (GetSecureStringFromData(
                            item.Data,
                            out SecureString outSecureString))
                        {
                            outList.Add(
                                new KeyValuePair<string, object>(
                                    RemoveTag(item.Name),
                                    outSecureString));
                        }
                        break;

                    case PSCredentialType:
                        if (ReadPSCredential(
                            item.Data,
                            out object credential))
                        {
                            outList.Add(
                                new KeyValuePair<string, object>(
                                    RemoveTag(item.Name),
                                    credential));
                        }
                        break;

                    case HashtableType:
                        if (ReadHashtable(
                            item.Name,
                            item.Data,
                            out object hashtable,
                            ref errorCode))
                        {
                            outList.Add(
                                new KeyValuePair<string, object>(
                                    RemoveTag(item.Name),
                                    hashtable));
                        }
                        break;
                }
            }

            outObjects = outList.ToArray();
            return true;
        }

        /// <summary>
        /// Delete vault object.
        /// </summary>
        /// <param name="name">Name of vault item to delete.</param>
        /// <param name="errorCode">Error code or zero.</param>
        /// <returns>True if object successfully deleted.</returns>
        public static bool DeleteObject(
            string name,
            ref int errorCode)
        {
            // Hash tables are complex and require special processing.
            if (!ReadObject(
                name,
                out object outObject,
                ref errorCode))
            {
                return false;
            }

            name = PrependTag(name);

            switch (outObject)
            {
                case Hashtable hashtable:
                    return DeleteHashtable(
                        name,
                        ref errorCode);

                default:
                    return DeleteBlob(
                        name,
                        ref errorCode);
            }
        }

        /// <summary>
        /// Returns an error message based on provided error code.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        /// <returns>Error message.</returns>
        public static string GetErrorMessage(int errorCode)
        {
            switch ((uint)errorCode)
            {
                case NativeUtils.PS_ERROR_BUFFER_TOO_LARGE:
                    return nameof(NativeUtils.PS_ERROR_BUFFER_TOO_LARGE);
                
                case NativeUtils.ERROR_BAD_USERNAME:
                    return nameof(NativeUtils.ERROR_BAD_USERNAME);

                case NativeUtils.ERROR_INVALID_FLAGS:
                    return nameof(NativeUtils.ERROR_INVALID_FLAGS);

                case NativeUtils.ERROR_INVALID_PARAMETER:
                    return nameof(NativeUtils.ERROR_INVALID_PARAMETER);

                case NativeUtils.ERROR_NOT_FOUND:
                    return nameof(NativeUtils.ERROR_NOT_FOUND);

                case NativeUtils.ERROR_NO_SUCH_LOGON_SESSION:
                    return nameof(NativeUtils.ERROR_NO_SUCH_LOGON_SESSION);

                case NativeUtils.SCARD_E_NO_READERS_AVAILABLE:
                    return nameof(NativeUtils.SCARD_E_NO_READERS_AVAILABLE);

                case NativeUtils.SCARD_E_NO_SMARTCARD:
                    return nameof(NativeUtils.SCARD_E_NO_SMARTCARD);

                case NativeUtils.SCARD_W_REMOVED_CARD:
                    return nameof(NativeUtils.SCARD_W_REMOVED_CARD);

                case NativeUtils.SCARD_W_WRONG_CHV:
                    return nameof(NativeUtils.SCARD_W_WRONG_CHV);
                
                default:
                    // TODO: Localize
                    return string.Format(CultureInfo.InvariantCulture, "Unknown error code: {0}", errorCode);
            }
        }

        #endregion

        #region Private methods

        #region Helper methods

        private static string PrependTag(string str)
        {
            return PSTag + str;
        }

        private static bool IsTagged(string str)
        {
            return str.StartsWith(PSTag);
        }

        private static string RemoveTag(string str)
        {
            if (IsTagged(str))
            {
                return str.Substring(PSTag.Length);
            }

            return str;
        }

        private static string PrependHTTag(
            string hashName,
            string keyName)
        {
            return PSHashtableTag + hashName + keyName;
        }

        private static string RecoverKeyname(
            string str,
            string hashName)
        {
            return str.Substring((PSHashtableTag + hashName).Length);
        }

        private static bool GetSecureStringFromData(
            byte[] data,
            out SecureString outSecureString)
        {
            if ((data.Length % 2) != 0)
            {
                Dbg.Assert(false, "Blob length for SecureString secure must be even.");
                outSecureString = null;
                return false;
            }

            outSecureString = new SecureString();
            var strLen = data.Length / 2;
            for (int i=0; i < strLen; i++)
            {
                int index = (2 * i);

                var ch = (char)(data[index + 1] * 256 + data[index]);
                outSecureString.AppendChar(ch);
            }

            return true;
        }

        private static bool GetDataFromSecureString(
            SecureString secureString,
            out byte[] data)
        {
            IntPtr ptr = Marshal.SecureStringToCoTaskMemUnicode(secureString);

            if (ptr != IntPtr.Zero)
            {
                try
                {
                    data = new byte[secureString.Length * 2];
                    Marshal.Copy(ptr, data, 0, data.Length);
                    return true;
                }
                finally
                {
                    Marshal.ZeroFreeCoTaskMemUnicode(ptr);
                }
            }

            data = null;
            return false;
        }

        #endregion

        #region Blob methods

        private static bool WriteBlob(
            string name,
            byte[] blob,
            string typeName,
            ref int errorCode)
        {
            bool success = false;
            var blobHandle = GCHandle.Alloc(blob, GCHandleType.Pinned);
            var credPtr = IntPtr.Zero;

            try
            {
                var credential = new NativeUtils.CREDENTIALA();
                credential.Type = (uint) NativeUtils.CRED_TYPE.GENERIC;
                credential.TargetName = name;
                credential.Comment = typeName;
                credential.CredentialBlobSize = (uint) blob.Length;
                credential.CredentialBlob = blobHandle.AddrOfPinnedObject();
                credential.Persist = (uint) NativeUtils.CRED_PERSIST.LOCAL_MACHINE;

                credPtr = Marshal.AllocHGlobal(Marshal.SizeOf(credential));
                Marshal.StructureToPtr<NativeUtils.CREDENTIALA>(credential, credPtr, false);

                success = NativeUtils.CredWriteW(
                    Credential: credPtr, 
                    Flags: 0);
                
                errorCode = Marshal.GetLastWin32Error();
            }
            finally
            {
                blobHandle.Free();
                if (credPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(credPtr);
                }
            }

            return success;
        }

        private static bool ReadBlob(
            string name,
            out byte[] blob,
            out string typeName,
            ref int errorCode)
        { 
            blob = null;
            typeName = null;
            var success = false;

            // Read Credential structure from vault given provided name.
            IntPtr credPtr = IntPtr.Zero;
            try
            {
                success = NativeUtils.CredReadW(
                    TargetName: name,
                    Type: (int) NativeUtils.CRED_TYPE.GENERIC,
                    Flags: 0,
                    Credential: out credPtr);

                errorCode = Marshal.GetLastWin32Error();

                if (success)
                {
                    // Copy returned credential to managed memory.
                    var credential = Marshal.PtrToStructure<NativeUtils.CREDENTIALA>(credPtr);
                    typeName = credential.Comment;

                    // Copy returned blob from credential structure.
                    var ansiString = Marshal.PtrToStringAnsi(
                        ptr: credential.CredentialBlob,
                        len: (int) credential.CredentialBlobSize);
                    blob = Encoding.ASCII.GetBytes(ansiString);
                }
            }
            finally
            {
                if (credPtr != IntPtr.Zero)
                {
                    NativeUtils.CredFree(credPtr);
                }
            }

            return success;
        }

        private struct EnumeratedBlob
        {
            public string Name;
            public string TypeName;
            public byte[] Data;
        }

        private static bool EnumerateBlobs(
            string filter,
            out EnumeratedBlob[] blobs,
            ref int errorCode)
        {
            blobs = null;
            var success = false;

            int count = 0;
            IntPtr credPtrPtr = IntPtr.Zero;
            try
            {
                success = NativeUtils.CredEnumerateW(
                    Filter: filter,
                    Flags: 0,
                    Count: out count,
                    Credentials: out credPtrPtr);

                errorCode = Marshal.GetLastWin32Error();

                if (success)
                {
                    List<EnumeratedBlob> blobArray = new List<EnumeratedBlob>(count);

                    // The returned credPtrPtr is an array of credential pointers.
                    for (int i=0; i<count; i++)
                    {
                        IntPtr credPtr = Marshal.ReadIntPtr(credPtrPtr, (i*IntPtr.Size));

                        // Copy returned credential to managed memory.
                        var credential = Marshal.PtrToStructure<NativeUtils.CREDENTIALA>(credPtr);

                        if (credential.CredentialBlob != IntPtr.Zero)
                        {
                            // Copy returned blob from credential structure.
                            var ansiString = Marshal.PtrToStringAnsi(
                                ptr: credential.CredentialBlob,
                                len: (int) credential.CredentialBlobSize);

                            blobArray.Add(
                                new EnumeratedBlob {
                                    Name = credential.TargetName, 
                                    TypeName = credential.Comment,
                                    Data = Encoding.ASCII.GetBytes(ansiString)
                                });
                        }
                    }

                    blobs = blobArray.ToArray();
                }
            }
            finally
            {
                if (credPtrPtr != IntPtr.Zero)
                {
                    NativeUtils.CredFree(credPtrPtr);
                }
            }

            return success;
        }

        private static bool DeleteBlob(
            string name,
            ref int errorCode)
        {
            var success = NativeUtils.CredDeleteW(
                TargetName: name,
                Type: (int) NativeUtils.CRED_TYPE.GENERIC,
                Flags: 0);

            errorCode = Marshal.GetLastWin32Error();

            return success;
        }

        #endregion

        #region String methods

        private static bool WriteString(
            string name,
            string strToWrite,
            ref int errorCode)
        {
            return WriteBlob(
                name: name,
                blob: Encoding.UTF8.GetBytes(strToWrite),
                typeName: StringType,
                errorCode: ref errorCode);
        }

        private static bool ReadString(
            byte[] blob,
            out object outString)
        {
            outString = Encoding.UTF8.GetString(blob);
            return true;
        }

        #endregion

        #region String array methods

        //
        // String arrays are stored as a blob:
        //  <arrayCount>    - number of strings in array (sizeof(int32))
        //  <length1>       - length of first string     (sizeof(int32))
        //  <string1>       - first string bytes         (length1)
        //  <length2>       - length of second string    (sizeof(int32))
        //  <string2>       - second string bytes        (length2)
        //  ...
        //

        private static bool WriteStringArray(
            string name,
            string[] strsToWrite,
            ref int errorCode)
        {
            // Compute blob size
            int arrayCount = strsToWrite.Length;
            int blobLength = sizeof(Int32) * (arrayCount + 1);
            int[] aStrSizeBytes = new int[arrayCount];
            int iCount = 0;
            foreach (string str in strsToWrite)
            {
                var strSizeBytes = Encoding.UTF8.GetByteCount(str);
                aStrSizeBytes[iCount++] = strSizeBytes;
                blobLength += strSizeBytes;
            }

            byte[] blob = new byte[blobLength];
            var index = 0;

            // Array count
            byte[] data = BitConverter.GetBytes(arrayCount);
            foreach (var b in data)
            {
                blob[index++] = b;
            }

            // Array strings
            iCount = 0;
            foreach (var str in strsToWrite)
            {
                // String length
                data = BitConverter.GetBytes(aStrSizeBytes[iCount++]);
                foreach (var b in data)
                {
                    blob[index++] = b;
                }

                // String bytes
                data = Encoding.UTF8.GetBytes(str);
                foreach (var b in data)
                {
                    blob[index++] = b;
                }
            }

            Dbg.Assert(index == blobLength, "Blob size must be consistent");

            // Write blob
            return WriteBlob(
                name: name,
                blob: blob,
                typeName: HashtableType,
                errorCode: ref errorCode);
        }

        private static void ReadStringArray(
            byte[] blob,
            out string[] outStrArray)
        {
            int index = 0;
            int arrayCount = BitConverter.ToInt32(blob, index);
            index += sizeof(Int32);

            outStrArray = new string[arrayCount];
            for (int iCount = 0; iCount < arrayCount; iCount++)
            {
                int strSizeBytes = BitConverter.ToInt32(blob, index);
                index += sizeof(Int32);

                outStrArray[iCount] = Encoding.UTF8.GetString(blob, index, strSizeBytes);
                index += strSizeBytes;
            }

            Dbg.Assert(index == blob.Length, "Blob length must be consistent");
        }

        #endregion
    
        #region SecureString methods

        private static bool WriteSecureString(
            string name,
            SecureString strToWrite,
            ref int errorCode)
        {
            if (GetDataFromSecureString(
                secureString: strToWrite,
                data: out byte[] data))
            {
                try
                {
                    return WriteBlob(
                        name: name,
                        blob: data,
                        typeName: SecureStringType,
                        errorCode: ref errorCode);
                }
                finally
                {
                    // Zero out SecureString data.
                    for (int i = 0; i < data.Length; i++)
                    {
                        data[i] = 0;
                    }
                }
            }
            
            return false;
        }

        private static bool ReadSecureString(
            byte[] ssBlob,
            out object outSecureString)
        {
            try
            {
                if (GetSecureStringFromData(
                    data: ssBlob, 
                    outSecureString: out SecureString outString))
                {
                    outSecureString = outString;
                    return true;
                }
            }
            finally
            {
                // Zero out blob data
                for (int i=0; i < ssBlob.Length; i++)
                {
                    ssBlob[0] = 0;
                }
            }

            outSecureString = null;
            return false;
        }

        #endregion

        #region PSCredential methods

        //
        // PSCredential blob packing:
        //      <offset>    Contains offset to password data        Length: sizeof(int)
        //      <userName>  Contains UserName string bytes          Length: userData bytes
        //      <password>  Contains Password SecureString bytes    Length: ssData bytes
        //

        private static bool WritePSCredential(
            string name,
            PSCredential credential,
            ref int errorCode)
        {
            if (GetDataFromSecureString(
                secureString: credential.Password,
                data: out byte[] ssData))
            {
                byte[] blob = null;
                try
                {
                    // Get username string bytes
                    var userData = Encoding.UTF8.GetBytes(credential.UserName);

                    // Create offset bytes to SecureString data
                    var offset = userData.Length + sizeof(Int32);
                    var offsetData = BitConverter.GetBytes(offset);

                    // Create blob
                    blob = new byte[offset + ssData.Length];

                    // Copy all to blob
                    var index = 0;
                    foreach (var b in offsetData)
                    {
                        blob[index++] = b;
                    }
                    foreach (var b in userData)
                    {
                        blob[index++] = b;
                    }
                    foreach (var b in ssData)
                    {
                        blob[index++] = b;
                    }

                    // Write blob
                    return WriteBlob(
                        name: name,
                        blob: blob,
                        typeName: PSCredentialType,
                        errorCode: ref errorCode);
                }
                finally
                {
                    // Zero out SecureString data
                    for (int i = 0; i < ssData.Length; i++)
                    {
                        ssData[i] = 0;
                    }
                    
                    // Zero out blob data
                    if (blob != null)
                    {
                        for (int i = 0; i < blob.Length; i++)
                        {
                            blob[i] = 0;
                        }
                    }
                }
            }
            
            return false;
        }

        private static bool ReadPSCredential(
            byte[] blob,
            out object credential)
        {
            byte[] ssData = null;

            try
            {
                // UserName
                var offset = BitConverter.ToInt32(blob, 0);
                int index = sizeof(Int32);
                var userName = Encoding.UTF8.GetString(blob, index, (offset - index));

                // SecureString
                ssData = new byte[(blob.Length - offset)];
                index = 0;
                for (int i = offset; i < blob.Length; i++)
                {
                    ssData[index++] = blob[i];
                }

                if (GetSecureStringFromData(
                    ssData,
                    out SecureString secureString))
                {
                    credential = new PSCredential(userName, secureString);
                    return true;
                }
            }
            finally
            {
                // Zero out data
                for (int i = 0; i < blob.Length; i++)
                {
                    blob[i] = 0;
                }
                if (ssData != null)
                {
                    for (int i = 0; i < ssData.Length; i++)
                    {
                        ssData[i] = 0;
                    }
                }
            }

            credential = null;
            return false;
        }

        #endregion

        #region Hashtable methods

        //
        // Hash table values will be limited to the currently supported secret types:
        //  byte[]
        //  string
        //  SecureString
        //  PSCredential
        //
        // The values are stored as separate secrets with special name tags.
        //  <secretName1>
        //  <secretName2>
        //  <secretName3>
        //   ...
        //
    
        private static bool WriteHashtable(
            string name,
            Hashtable hashtable,
            ref int errorCode)
        {
            // Impose size limit
            if (hashtable.Count > MaxHashtableItemCount)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, 
                        "The provided Hashtable, {0}, has too many entries. The maximum number of entries is {1}.",
                        name, MaxHashtableItemCount));
            }

            // Create a list of hashtable entries.
            var entries = new Dictionary<string, object>();
            foreach (var key in hashtable.Keys)
            {
                var entry = hashtable[key];
                if (entry is PSObject psObjectEntry)
                {
                    entry = psObjectEntry.BaseObject;
                }
                var entryType = entry.GetType();
                if (entryType == typeof(byte[]) ||
                    entryType == typeof(string) ||
                    entryType == typeof(SecureString) ||
                    entryType == typeof(PSCredential))
                {
                    var entryName = PrependHTTag(name, key.ToString());
                    entries.Add(entryName, entry);
                }
                else
                {
                    throw new ArgumentException(
                        string.Format(CultureInfo.InstalledUICulture, 
                        "The object type for {0} Hashtable entry is not supported. Supported types are byte[], string, SecureString, PSCredential",
                        key));
                }
            }

            // Write the member name array.
            var hashTableEntryNames = new List<string>();
            foreach (var entry in entries)
            {
                hashTableEntryNames.Add(entry.Key);
            }
            if (!WriteStringArray(
                name: name,
                strsToWrite: hashTableEntryNames.ToArray(),
                errorCode: ref errorCode))
            {
                return false;
            }

            // Write each entry as a separate secret.  Roll back on any failure.
            var success = false;
            try
            {
                foreach (var entry in entries)
                {
                    success = WriteObjectImpl(
                        name: entry.Key,
                        objectToWrite: entry.Value,
                        errorCode: ref errorCode);
                    
                    if (!success)
                    {
                        break;
                    }
                }

                return success;
            }
            finally
            {
                if (!success)
                {
                    // Roll back.
                    // Remove any Hashtable secret that was written, ignore errors.
                    int error = 0;
                    foreach (var entry in entries)
                    {
                        DeleteBlob(
                            name: entry.Key,
                            errorCode: ref error);
                    }

                    // Remove the Hashtable member names.
                    DeleteBlob(
                        name: name,
                        ref error);
                }
            }
        }

        private static bool ReadHashtable(
            string name,
            byte[] blob,
            out object outHashtable,
            ref int errorCode)
        {
            // Get array of Hashtable secret names.
            ReadStringArray(
                blob,
                out string[] entryNames);
            
            outHashtable = null;
            var hashtable = new Hashtable();
            foreach (var entryName in entryNames)
            {
                if (!ReadObjectImpl(
                    entryName,
                    out object outObject,
                    ref errorCode))
                {
                    return false;
                }

                hashtable.Add(
                    RecoverKeyname(entryName, name),
                    outObject);
            }

            outHashtable = hashtable;
            return true;
        }

        private static bool DeleteHashtable(
            string name,
            ref int errorCode)
        {
            // Get array of Hashtable secret names.
            if (!ReadBlob(
                name,
                out byte[] blob,
                out string typeName,
                ref errorCode))
            {
                return false;
            }

            ReadStringArray(
                blob,
                out string[] entryNames);

            // Delete each Hashtable entry secret.
            foreach (var entryName in entryNames)
            {
                DeleteBlob(
                    name: entryName,
                    ref errorCode);
            }

            // Delete the Hashtable secret names list.
            DeleteBlob(
                name: name,
                ref errorCode);

            return true;
        }

        #endregion
    
        #endregion
    }

    #endregion
#else
    #region Keyring

    // TODO: Implement via Gnome Keyring

    #endregion
#endif

    #region SecretsManagementExtension class

    /// <summary>
    /// Abstract class which SecretsManagement extension vault modules will implement
    /// to provide secret management functions for plugin local or remote vaults.
    /// </summary>
    public abstract class SecretsManagementExtension
    {
        #region Properties

        /// <summary>
        /// Name of the registered vault associated with this extension instance.
        /// </summary>
        public string VaultName { get; }

        #endregion

        #region Constructor

        private SecretsManagementExtension() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SecretsManagementExtension"/> class.
        /// </summary>
        public SecretsManagementExtension(string vaultName)
        {
            if (string.IsNullOrEmpty(vaultName))
            {
                throw new ArgumentNullException("vaultName");
            }

            VaultName = vaultName;
        }

        #endregion

        #region Abstract methods

        /// <summary>
        /// Adds a secret to the vault.
        /// Currently supported secret types are:
        ///     PSCredential
        ///     SecureString
        ///     String
        ///     Hashtable
        ///     byte[]
        /// </summary>
        /// <param name="name">Name under which secret will be stored.</param>
        /// <param name="secret">Secret to be stored.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>True on success.</returns>
        public abstract bool SetSecret(
            string name,
            object secret,
            out Exception error);

        /// <summary>
        /// Gets a secret from the vault.
        /// </summary>
        /// <param name="name">Name of the secret to retrieve.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>Secret object retrieved from the vault.  Null returned if not found.</returns>
        public abstract object GetSecret(
            string name,
            out Exception error);
        
        /// <summary>
        /// Removes a secret from the vault.
        /// </summary>
        /// <param name="name">Name of the secret to remove.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>True on success.</returns>
        public abstract bool RemoveSecret(
            string name,
            out Exception error);

        /// <summary>
        /// Returns a list of key/value pairs for each found vault secret, where
        ///     key   (string): is the name of the secret.
        ///     value (object): is the corresponding secret object.
        /// </summary>
        /// <param name="filter">
        /// A string, including wildcard characters, used to search secret names.
        /// A null value, empty string, or "*" will return all vault secrets.
        /// </param>
        /// <param name="secrets">Array of returned secret name/value pairs.</param>
        /// <param name="error">Optional exception object on failure.</param>
        public abstract void EnumerateSecrets(
            string filter,
            out KeyValuePair<string, object>[] secrets,
            out Exception error);

        #endregion

        #region Helper methods

        /// <summay>
        /// Returns a key/value dictionary of parameters stored in the local secure store.
        /// This can be used to store any secrets needed access underlying vault.
        /// </summary>
        /// <param name="paramsName">Name of stored parameters</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>Dictionary of retrieved parameter key/value pairs or null if not found.</returns>
        internal IReadOnlyDictionary<string, object> GetParameters(
            string paramsName,
            out Exception error)
        {
            // Construct unique name for parameters based on vault name.
            //  e.g., "_SPT_VaultName_ParamsName_"
            var fullName = "_SPT_" + VaultName + "_" + paramsName + "_";
            int errorCode = 0;
            if (!LocalSecretStore.ReadObject(
                    paramsName,
                    out object outObject,
                    ref errorCode))
            {
                var msg = LocalSecretStore.GetErrorMessage(errorCode);
                error = new InvalidOperationException(msg);
                return null;
            }
            
            error = null;
            var parametersHash = outObject as Hashtable;
            var parameters = new Dictionary<string, object>(parametersHash.Count);
            foreach (var key in parametersHash.Keys)
            {
                parameters.Add((string)key, parametersHash[key]);
            }
            return new ReadOnlyDictionary<string, object>(parameters);
        }

        #endregion
    }

    #endregion

    #region Extension vault module class

    /// <summary>
    /// Class that contains all vault module information and secret manipulation methods.
    /// </summary>
    internal class ExtensionVaultModule
    {
        //
        // Default commands:
        //
        //  # Retrieve secret object from vault and returns
        //  # a collection of PSCustomObject with properties:
        //  #   Name:  <secretName>     (string)
        //  #   Value: <secretValue>    (object)
        //  Get-Secret (required)
        //      [string] $Name
        //
        //  # Add secret object to vault.
        //  Set-Secret (optional)
        //      [string] $Name,
        //      [PSObject] $secret
        //
        //  # Remove secret object from vault.
        //  Remove-Secret (optional)
        //      [string] $Name
        //

        #region Members

        #region Strings

        internal const string DefaultGetSecretCmd = "Get-Secret";
        internal const string DefaultSetSecretCmd = "Set-Secret";
        internal const string DefaultRemoveSecretCmd = "Remove-Secret";

        internal const string ModuleNameStr = "ModuleName";
        internal const string ModulePathStr = "ModulePath";
        internal const string GetSecretScriptStr = "GetSecretScript";
        internal const string GetSecretParamsStr = "GetSecretParamsName";
        internal const string SetSecretScriptStr = "SetSecretScript";
        internal const string SetSecretParamsStr = "SetSecretParamsName";
        internal const string RemoveSecretScriptStr = "RemoveSecretScriptStr";
        internal const string RemoveSecretParamsStr = "RemoveSecretParamsName";
        internal const string HaveGetCmdletStr = "HaveGetCmdlet";
        internal const string HaveSetCmdletStr = "HaveSetCmdlet";
        internal const string HaveRemoveCmdletStr = "HaveRemoveCmdlet";

        private const string RunCommandScript = @"
            param(
                [string] $ModulePath,
                [string] $ModuleName,
                [string] $CommandName,
                [hashtable] $Params
            )

            Import-Module -Name $ModulePath -Scope Local -Force
            & ""$ModuleName\$CommandName"" @Params
        ";

        private const string RunScriptScript = @"
            param (
                [ScriptBlock] $sb,
                [hashtable] $Params
            )

            if ($Params -ne $null)
            {
                & $sb @Params
            }
            else
            {
                & $sb
            }
        ";

        #endregion

        private ScriptBlock _GetSecretScriptBlock;
        private ScriptBlock _SetSecretScriptBlock;
        private ScriptBlock _RemoveSecretScriptBlock;

        #endregion

        #region Properties

        /// <summary>
        /// Name of extension vault.
        /// </summary>
        public string VaultName { get; }

        /// <summary>
        /// Module name to qualify module commands.
        /// </summary>
        public string ModuleName { get; }

        /// <summary>
        /// Module path.
        /// </summary>
        public string ModulePath { get; }

        /// <summary>
        /// Optional script to get secret from vault.
        /// <summary>
        public string GetSecretScript { get; }

        /// <summary>
        /// Optional local store name for get secret script parameters.
        /// <summary>
        public string GetSecretParamsName { get; }

        /// <summary>
        /// Optional script to add secret to vault.
        /// </summary>
        public string SetSecretScript { get; }

        /// <summary>
        /// Optional local store name for set secret script parameters.
        /// <summary>
        public string SetSecretParamsName { get; }

        /// <summary>
        /// Optional script to remove secret from vault.
        /// </summary>
        public string RemoveSecretScript { get; }

        /// <summary>
        /// Optional local store name for remove secret script parameters.
        /// </summary>
        public string RemoveSecretParamsName { get; }

        public bool HaveGetCommand { get; }
        public bool HaveSetCommand { get; }
        public bool HaveRemoveCommand { get; }

        #endregion

        #region Constructor

        private ExtensionVaultModule() { }

        /// <summary>
        /// Initializes a new instance of ExtensionVaultModule
        /// </summary>
        public ExtensionVaultModule(string vaultName, Hashtable vaultInfo)
        {
            // Required module information.
            VaultName = vaultName;
            ModuleName = (string) vaultInfo[ModuleNameStr];
            ModulePath = (string) vaultInfo[ModulePathStr];
            HaveGetCommand = (bool) vaultInfo[HaveGetCmdletStr];
            HaveSetCommand = (bool) vaultInfo[HaveSetCmdletStr];
            HaveRemoveCommand = (bool) vaultInfo[HaveRemoveCmdletStr];

            // Optional Get-Secret script block.
            GetSecretScript = (vaultInfo.ContainsKey(GetSecretScriptStr)) ?
                (string) vaultInfo[GetSecretScriptStr] : string.Empty;
            GetSecretParamsName = (vaultInfo.ContainsKey(GetSecretParamsStr)) ?
                (string) (string) vaultInfo[GetSecretParamsStr] : string.Empty;

            // Optional Set-Secret script block.
            SetSecretScript = (vaultInfo.ContainsKey(SetSecretScriptStr)) ?
                (string) vaultInfo[SetSecretScriptStr] : string.Empty;
            SetSecretParamsName = (vaultInfo.ContainsKey(SetSecretParamsStr)) ?
                (string) vaultInfo[SetSecretParamsStr] : string.Empty;

            // Optional Remove-Secret script block.
            RemoveSecretScript = (vaultInfo.ContainsKey(RemoveSecretScriptStr)) ?
                (string) vaultInfo[RemoveSecretScriptStr] : string.Empty;
            RemoveSecretParamsName = (vaultInfo.ContainsKey(RemoveSecretParamsStr)) ?
                (string) vaultInfo[RemoveSecretParamsStr] : string.Empty;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Invokes module command to get secret from this vault.
        /// <summary>
        /// <param name="cmdlet">PowerShell cmdlet to stream data.</param>
        /// <param name="name">Name of secret to get.</param>
        /// <returns>Collection of invocation results.</returns>
        public PSDataCollection<PSObject> InvokeGetSecret(
            PSCmdlet cmdlet,
            string name)
        {
            // Required parameter.
            Hashtable parameters = new Hashtable() {
                { "Name", name }
            };

            if (HaveGetCommand)
            {
                return PowerShellInvoker.InvokeScript(
                    cmdlet: cmdlet,
                    script: RunCommandScript,
                    args: new object[] { ModulePath, ModuleName, DefaultGetSecretCmd, parameters });
            }

            // Get stored script parameters if provided.
            var additionalParameters = GetParamsFromStore(GetSecretParamsName);
            if (additionalParameters != null)
            {
                foreach (var key in additionalParameters.Keys)
                {
                    parameters.Add((string) key, additionalParameters[key]);
                }
            }

            // Use provided secret get script.
            if (_GetSecretScriptBlock == null)
            {
                // TODO: !! Add support for creation of *untrusted* script block. !!
                _GetSecretScriptBlock = ScriptBlock.Create(GetSecretScript);
            }

            return PowerShellInvoker.InvokeScript(
                cmdlet: cmdlet,
                script: RunScriptScript,
                args: new object[] { _GetSecretScriptBlock, parameters });
        }

        /// <summary>
        /// Invokes module command to add a secret to this vault.
        /// </summary>
        /// <param name="cmdlet">PowerShell cmdlet to stream data.</param>
        /// <param name="name">Name of secret to add.</param>
        /// <param name="secret">Secret object to add to vault.</param>
        public void InvokeSetSecret(
            PSCmdlet cmdlet,
            string name,
            object secret)
        {
            Hashtable parameters = new Hashtable() {
                { "Name", name },
                { "Secret", secret }
            };

            if (HaveSetCommand)
            {
                cmdlet.WriteObject(
                    PowerShellInvoker.InvokeScript(
                        cmdlet: cmdlet,
                        script: RunScriptScript,
                        args: new object[] { _GetSecretScriptBlock, parameters }));
                return;
            }

            // Get stored script parameters if provided.
            var additionalParameters = GetParamsFromStore(SetSecretParamsName);
            if (additionalParameters != null)
            {
                foreach (var key in additionalParameters.Keys)
                {
                    parameters.Add((string) key, additionalParameters[key]);
                }
            }

            // Use provided secret get script.
            if (_SetSecretScriptBlock == null)
            {
                // TODO: !! Add support for creation of *untrusted* script block. !!
                _SetSecretScriptBlock = ScriptBlock.Create(SetSecretScript);
            }

            cmdlet.WriteObject(
                PowerShellInvoker.InvokeScript(
                    cmdlet: cmdlet,
                    script: RunScriptScript,
                    args: new object[] { _SetSecretScriptBlock, parameters }));
        }

        /// <summary>
        /// Invokes module command to remove a secret from this vault.
        /// </summary>
        /// <param name="cmdlet">PowerShell cmdlet to stream data.</param>
        /// <param name="name">Name of secret to remove.</param>
        public void InvokeRemoveSecret(
            PSCmdlet cmdlet,
            string name)
        {
            Hashtable parameters = new Hashtable() {
                { "Name", name }
            };

            if (HaveRemoveCommand)
            {
                cmdlet.WriteObject(
                    PowerShellInvoker.InvokeScript<PSObject>(
                        script: RunCommandScript,
                        mergeDataStreamsToOutput: true,
                        args: new object[] { ModulePath, ModuleName, DefaultRemoveSecretCmd, parameters },
                        dataStreams: out _));
                return;
            }

            // Get stored script parameters if provided.
            var additionalParameters = GetParamsFromStore(RemoveSecretParamsName);
            if (additionalParameters != null)
            {
                foreach (var key in additionalParameters.Keys)
                {
                    parameters.Add((string) key, additionalParameters[key]);
                }
            }

            // Use provided secret get script.
            if (_RemoveSecretScriptBlock == null)
            {
                // TODO: !! Add support for creation of *untrusted* script block. !!
                _RemoveSecretScriptBlock = ScriptBlock.Create(RemoveSecretScript);
            }

            cmdlet.WriteObject(
                PowerShellInvoker.InvokeScript<PSObject>(
                    script: RunScriptScript,
                    mergeDataStreamsToOutput: true,
                    args: new object[] { _RemoveSecretScriptBlock, parameters },
                    dataStreams: out _));
        }

        #endregion

        #region Private methods

        private static Hashtable GetParamsFromStore(string paramsName)
        {
            Hashtable parameters = null;
            if (!string.IsNullOrEmpty(paramsName))
            {
                int errorCode = 0;
                if (LocalSecretStore.ReadObject(
                    paramsName,
                    out object outObject,
                    ref errorCode))
                {
                    parameters = outObject as Hashtable;
                }
            }

            return parameters;
        }

        #endregion
    }

    #endregion

    #region PowerShellInvoker

    internal static class PowerShellInvoker
    {
        #region Members

        private static readonly object _syncObject;
        private static System.Management.Automation.PowerShell _powershell;
        
        #endregion

        #region Constructor

        static PowerShellInvoker()
        {
            _syncObject = new object();
            _powershell = System.Management.Automation.PowerShell.Create();
        }

        #endregion

        #region Public methods

        // TODO: Create a PowerShellInvoker instance (not static) and assign to static
        // variable.  Then let cmdlet variable reference be settable and still used in
        // the data stream handler enclosures.
        public static PSDataCollection<PSObject> InvokeScript(
            PSCmdlet cmdlet,
            string script,
            object[] args)
        {
            using (var powershell = System.Management.Automation.PowerShell.Create())
            using (var waitData = new AutoResetEvent(false))
            using (var dataStream = new PSDataCollection<PSStreamObject>())
            {
                // Handle streaming data
                dataStream.DataAdded += (sender, dataStreamArgs) => {
                    waitData.Set();
                };
                powershell.Streams.Error.DataAdded += (sender, errorStreamArgs) => {
                    foreach (var error in powershell.Streams.Error.ReadAll())
                    {
                        dataStream.Add(
                            new PSStreamObject(PSStreamObjectType.Error, error));
                    }
                };
                powershell.Streams.Warning.DataAdded += (sender, warningStreamArgs) => {
                    foreach (var warning in powershell.Streams.Warning.ReadAll())
                    {
                        dataStream.Add(
                            new PSStreamObject(PSStreamObjectType.Warning, warning.Message));
                    }
                };
                powershell.Streams.Verbose.DataAdded += (sender, verboseStreamArgs) => {
                    foreach (var verboseItem in powershell.Streams.Verbose.ReadAll())
                    {
                        dataStream.Add(
                            new PSStreamObject(PSStreamObjectType.Verbose, verboseItem.Message));
                    }
                };

                powershell.AddScript(script).AddParameters(args);
                var async = powershell.BeginInvoke<PSObject>(null);

                // Wait for script to complete while writing streaming data on cmdlet thread.
                var waitHandles = new WaitHandle[]
                {
                    waitData,
                    async.AsyncWaitHandle
                };
                while (true)
                {
                    var index = WaitHandle.WaitAny(waitHandles);
                    switch (index)
                    {
                        case 0:
                            // Data available
                            foreach (var item in dataStream.ReadAll())
                            {
                                item.WriteStreamObject(cmdlet: cmdlet, overrideInquire: true);
                            }
                            break;

                        case 1:
                            // Script execution complete
                            var results = powershell.EndInvoke(async);
                            return results;

                        default:
                            return null;
                    }
                }
            }
        }

        public static Collection<T> InvokeScript<T>(
            string script,
            object[] args,
            out PSDataStreams dataStreams,
            bool mergeDataStreamsToOutput = false)
        {
            lock (_syncObject)
            {
                // Recreate _powershell/Runspace if needed.
                if ((_powershell.InvocationStateInfo.State != PSInvocationState.Completed && 
                     _powershell.InvocationStateInfo.State != PSInvocationState.NotStarted)
                     || (_powershell.Runspace.RunspaceStateInfo.State != RunspaceState.Opened))
                {
                    _powershell.Dispose();
                    _powershell = System.Management.Automation.PowerShell.Create();
                }

                _powershell.Commands.Clear();
                _powershell.Streams.ClearStreams();
                _powershell.Runspace.ResetRunspaceState();

                _powershell.AddScript(script).AddParameters(args);
                if (mergeDataStreamsToOutput)
                {
                    _powershell.Commands.Commands[0].MergeMyResults(PipelineResultTypes.All, PipelineResultTypes.Output);
                }

                var results = _powershell.Invoke<T>();
                dataStreams = _powershell.Streams;
                return results;
            }
        }

        #endregion
    }

    #endregion

    #region RegisteredVaultCache

    internal static class RegisteredVaultCache
    {
        #region Members

        #region Strings

        private const string ConvertJsonToHashtableScript = @"
            param (
                [string] $json
            )

            function ConvertToHash
            {
                param (
                    [pscustomobject] $object
                )

                $output = @{}
                $object | Get-Member -MemberType NoteProperty | ForEach-Object {
                    $name = $_.Name
                    $value = $object.($name)

                    if ($value -is [object[]])
                    {
                        $array = @()
                        $value | ForEach-Object {
                            $array += (ConvertToHash $_)
                        }
                        $output.($name) = $array
                    }
                    elseif ($value -is [pscustomobject])
                    {
                        $output.($name) = (ConvertToHash $value)
                    }
                    else
                    {
                        $output.($name) = $value
                    }
                }

                $output
            }

            $customObject = ConvertFrom-Json $json
            return ConvertToHash $customObject
        ";

        private static readonly string RegistryDirectoryPath =  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + 
            @"\Microsoft\Windows\PowerShell\SecretVaultRegistry";

        private static readonly string RegistryFilePath = RegistryDirectoryPath + @"\VaultInfo";

        #endregion

        private static readonly FileSystemWatcher _registryWatcher;
        private static readonly Dictionary<string, ExtensionVaultModule> _vaultCache;
        private static Hashtable _vaultInfoCache;
        private static bool _allowAutoRefresh;

        #endregion

        #region Properties

        /// <summary>
        /// Gets a dictionary of registered vault extensions, sorted by vault name.
        /// </summary>
        public static SortedDictionary<string, ExtensionVaultModule> VaultExtensions
        {
            get 
            {
                lock (_vaultInfoCache)
                {
                    var returnVaults = new SortedDictionary<string, ExtensionVaultModule>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var vaultName in _vaultCache.Keys)
                    {
                        returnVaults.Add(vaultName, _vaultCache[vaultName]);
                    }
                    return returnVaults;
                }
            }
        }

        #endregion

        #region Constructor

        static RegisteredVaultCache()
        {
            // Verify path or create.
            if (!Directory.Exists(RegistryDirectoryPath))
            {
                Directory.CreateDirectory(RegistryDirectoryPath);
            }

            _vaultInfoCache = new Hashtable();
            _vaultCache = new Dictionary<string, ExtensionVaultModule>(StringComparer.InvariantCultureIgnoreCase);

            // Create file watcher.
            _registryWatcher = new FileSystemWatcher(RegistryDirectoryPath);
            _registryWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _registryWatcher.Filter = "VaultInfo";
            _registryWatcher.EnableRaisingEvents = true;
            _registryWatcher.Changed += (sender, args) => { if (_allowAutoRefresh) { RefreshCache(); } };

            RefreshCache();
            _allowAutoRefresh = true;
        }

        #endregion

        #region Public methods


        /// <summary>
        /// Retrieve all vault items from cache.
        /// </summary>
        /// <returns>Hashtable of vault items.</returns>
        public static Hashtable GetAll()
        {
            lock (_vaultInfoCache)
            {
                var vaultItems = (Hashtable) _vaultInfoCache.Clone();
                return vaultItems;
            }
        }

        /// <summary>
        /// Add item to cache.
        /// </summary>
        /// <param name="vaultInfo">Hashtable of vault information.</param>
        /// <returns>True when item is successfully added.</returns>
        public static bool Add(
            string keyName,
            Hashtable vaultInfo)
        {
            var vaultItems = GetAll();
            if (!vaultItems.ContainsKey(keyName))
            {
                vaultItems.Add(keyName, vaultInfo);
                WriteSecretVaultRegistry(vaultItems);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove item from cache.
        /// </summary>
        /// <param name="keyName">Name of item to remove.</param>
        /// <returns>True when item is successfully removed.</returns>
        public static Hashtable Remove(string keyName)
        {
            var vaultItems = GetAll();
            if (vaultItems.ContainsKey(keyName))
            {
                Hashtable vaultInfo = (Hashtable) vaultItems[keyName];
                vaultItems.Remove(keyName);
                WriteSecretVaultRegistry(vaultItems);
                return vaultInfo;
            }

            return null;
        }

        #endregion

        #region Private methods

        private static void RefreshCache()
        {
            var vaultItems = ReadSecretVaultRegistry();

            lock (_vaultInfoCache)
            {
                _vaultInfoCache = vaultItems;

                _vaultCache.Clear();
                foreach (string vaultKey in _vaultInfoCache.Keys)
                {
                    _vaultCache.Add(
                        key: vaultKey, 
                        value: new ExtensionVaultModule(vaultKey, (Hashtable) _vaultInfoCache[vaultKey]));
                }
            }
        }

        private static Hashtable ConvertJsonToHashtable(string json)
        {
            var psObject = PowerShellInvoker.InvokeScript<PSObject>(
                script: ConvertJsonToHashtableScript,
                args: new object[] { json },
                dataStreams: out PSDataStreams _);

            return psObject[0].BaseObject as Hashtable;
        }

        /// <summary>
        /// Reads the current user secret vault registry information from file.
        /// </summary>
        /// <returns>Hashtable containing registered vault information.</returns>
        private static Hashtable ReadSecretVaultRegistry()
        {
            if (!File.Exists(RegistryFilePath))
            {
                return new Hashtable();
            }

            var count = 0;
            do
            {
                try
                {
                    string jsonInfo = File.ReadAllText(RegistryFilePath);
                    return ConvertJsonToHashtable(jsonInfo);
                }
                catch (IOException)
                {
                    // Make up to four attempts.
                }
                catch
                {
                    // Unknown error.
                    break;
                }

                System.Threading.Thread.Sleep(250);

            } while (++count < 4);

            Dbg.Assert(false, "Unable to read vault registry file!");
            return new Hashtable();
        }

        /// <summary>
        /// Writes the Hashtable registered vault information data to file as json.
        /// </summary>
        /// <param>Hashtable containing registered vault information.</param>
        private static void WriteSecretVaultRegistry(Hashtable dataToWrite)
        {
            var psObject = PowerShellInvoker.InvokeScript<PSObject>(
                script: @"param ([hashtable] $dataToWrite) ConvertTo-Json $dataToWrite",
                args: new object[] { dataToWrite },
                dataStreams: out PSDataStreams _);
            string jsonInfo = psObject[0].BaseObject as string;

            _allowAutoRefresh = false;
            try
            {
                var count = 0;
                do
                {
                    try
                    {
                        File.WriteAllText(RegistryFilePath, jsonInfo);
                        RefreshCache();
                        return;
                    }
                    catch (IOException)
                    {
                        // Make up to four attempts.
                    }
                    catch
                    {
                        // Unknown error.
                        break;
                    }

                    System.Threading.Thread.Sleep(250);

                } while (++count < 4);
            }
            finally
            {
                _allowAutoRefresh = true;
            }

            Dbg.Assert(false, "Unable to write vault registry file!");
        }

        #endregion
    }

    #endregion
}
