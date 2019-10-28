// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

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

        public static bool EnumerateObjectInfo(
            string filter,
            out KeyValuePair<string, string>[] outObjectInfos,
            ref int errorCode)
        {
            if (!EnumerateBlobs(
                PrependTag(filter),
                out EnumeratedBlob[] outBlobs,
                ref errorCode))
            {
                outObjectInfos = null;
                return false;
            }

            var outList = new List<KeyValuePair<string, string>>(outBlobs.Length);
            foreach (var item in outBlobs)
            {
                switch (item.TypeName)
                {
                    case ByteArrayType:
                        outList.Add(
                            new KeyValuePair<string, string>(
                                RemoveTag(item.Name),
                                nameof(SecretsManagementExtension.SupportedTypes.ByteArray)));
                        break;

                    case StringType:
                        outList.Add(
                            new KeyValuePair<string, string>(
                                RemoveTag(item.Name),
                                nameof(SecretsManagementExtension.SupportedTypes.String)));
                        break;

                    case SecureStringType:
                        outList.Add(
                            new KeyValuePair<string, string>(
                                RemoveTag(item.Name),
                                nameof(SecretsManagementExtension.SupportedTypes.SecureString)));
                        break;

                    case PSCredentialType:
                        outList.Add(
                            new KeyValuePair<string, string>(
                                RemoveTag(item.Name),
                                nameof(SecretsManagementExtension.SupportedTypes.PSCredential)));
                        break;

                    case HashtableType:
                        outList.Add(
                            new KeyValuePair<string, string>(
                                RemoveTag(item.Name),
                                nameof(SecretsManagementExtension.SupportedTypes.Hashtable)));
                        break;
                }

                // Delete local copy of blob.
                ZeroOutData(item.Data);
            }

            outObjectInfos = outList.ToArray();
            return true;
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

        private static void ZeroOutData(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = 0;
            }
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
                    ZeroOutData(data);
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
                ZeroOutData(ssBlob);
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
                    ZeroOutData(ssData);

                    if (blob != null)
                    {
                        ZeroOutData(blob);
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
                ZeroOutData(blob);
                
                if (ssData != null)
                {
                    ZeroOutData(ssData);
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
                if (ReadObjectImpl(
                    entryName,
                    out object outObject,
                    ref errorCode))
                {
                    hashtable.Add(
                    RecoverKeyname(entryName, name),
                    outObject);
                }
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
        #region Enums

        public enum SupportedTypes
        {
            Unknown = 0,
            ByteArray,
            String,
            SecureString,
            PSCredential,
            Hashtable
        }

        #endregion

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
        /// <param name="parameters">Optional additional parameters.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>True on success.</returns>
        public abstract bool SetSecret(
            string name,
            object secret,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error);

        /// <summary>
        /// Gets a secret from the vault.
        /// </summary>
        /// <param name="name">Name of the secret to retrieve.</param>
        /// <param name="parameters">Optional additional parameters.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>Secret object retrieved from the vault.  Null returned if not found.</returns>
        public abstract object GetSecret(
            string name,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error);
        
        /// <summary>
        /// Removes a secret from the vault.
        /// </summary>
        /// <param name="name">Name of the secret to remove.</param>
        /// <param name="parameters">Optional additional parameters.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>True on success.</returns>
        public abstract bool RemoveSecret(
            string name,
            IReadOnlyDictionary<string, object> parameters,
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
        /// <param name="parameters">Optional additional parameters.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>Array of secret name/typeName pairs.</returns>
        public abstract KeyValuePair<string, string>[] GetSecretInfo(
            string filter,
            IReadOnlyDictionary<string, object> parameters,
            out Exception error);

        /// <summary>
        /// Validates operation of the registered extension. 
        /// </summary>
        /// <param name="parameters">Optional parameters for validation.</param>
        /// <param name="error">Optional exception object on failure.</param>
        /// <returns>True if extension operates as expected.</returns>
        // TODO: Add this.
        //public abstract bool ValidateExtension(
        //    IReadOnlyDictionary<string, object> parameters,
        //    out Exception error);

        // TODO: Add this?
        // public abstract void StopOperation();

        #endregion
    }

    #endregion

    #region Extension vault module class

    /// <summary>
    /// Class that contains all vault module information and secret manipulation methods.
    /// </summary>
    internal class ExtensionVaultModule
    {
        #region Members

        internal const string GetSecretCmd = "Get-Secret";
        internal const string GetSecretInfoCmd = "Get-SecretInfo";
        internal const string SetSecretCmd = "Set-Secret";
        internal const string RemoveSecretCmd = "Remove-Secret";
        internal const string ModuleNameStr = "ModuleName";
        internal const string ModulePathStr = "ModulePath";
        internal const string VaultParametersStr = "VaultParameters";
        internal const string ImplementingTypeStr = "ImplementingType";
        internal const string ImplementingFunctionsStr = "ImplementingFunctions";

        private Lazy<SecretsManagementExtension> _vaultExtentsion;
        
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
        /// Name of the assembly implementing the SecretsManagementExtension derived type.
        /// </summary>
        public string ImplementingTypeAssemblyName { get; }

        /// <summary>
        /// Name of type implementing SecretsManagementExtension abstract class.
        /// </summary>
        public string ImplementingTypeName { get; }

        /// <summary>
        /// Optional local store name for additional vault parameters.
        /// <summary>
        public string VaultParametersName { get; }

        #endregion

        #region Constructor

        private ExtensionVaultModule() 
        {
        }

        /// <summary>
        /// Initializes a new instance of ExtensionVaultModule.
        /// </summary>
        public ExtensionVaultModule(
            string vaultName,
            Hashtable vaultInfo)
        {
            // Required module information.
            VaultName = vaultName;
            ModuleName = (string) vaultInfo[ModuleNameStr];
            ModulePath = (string) vaultInfo[ModulePathStr];

            var implementingType = (Hashtable) vaultInfo[ImplementingTypeStr];
            ImplementingTypeAssemblyName = (string) implementingType["AssemblyName"];
            ImplementingTypeName = (string) implementingType["TypeName"];

            VaultParametersName = (vaultInfo.ContainsKey(VaultParametersStr)) ?
                (string) (string) vaultInfo[VaultParametersStr] : string.Empty;

            Init();
        }

        /// <summary>
        /// Initializes a new instance of ExtensionVaultModule from an existing instance.
        /// </summary>
        public ExtensionVaultModule(
            ExtensionVaultModule module)
        {
            VaultName = module.VaultName;
            ModuleName = module.ModuleName;
            ModulePath = module.ModulePath;
            ImplementingTypeAssemblyName = module.ImplementingTypeAssemblyName;
            ImplementingTypeName = module.ImplementingTypeName;
            VaultParametersName = module.VaultParametersName;

            Init();
        }

        private void Init()
        {
            _vaultExtentsion = new Lazy<SecretsManagementExtension>(() => {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name.Equals(ImplementingTypeAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        var implementingType = assembly.GetType(ImplementingTypeName);
                        if (implementingType != null)
                        {
                            // SecretsManagementExtension abstract class constructor takes a single 'vaultName' parameter.
                            return (SecretsManagementExtension) Activator.CreateInstance(
                                type: implementingType,
                                args: new object[] { VaultName });
                        }
                    }
                }

                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, 
                        "Unable to find and create SecretsManagementExtension type instance from vault {0}", VaultName));
            });
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Invoke SetSecret method on vault extension.
        /// </summary>
        /// <param name="name">Name of secret to add.</param>
        /// <param name="secret">Secret object to add.</param>
        /// <param name="cmdlet">Calling cmdlet.</param>
        public void InvokeSetSecret(
            string name,
            object secret,
            PSCmdlet cmdlet)
        {
            if (!string.IsNullOrEmpty(this.ImplementingTypeName))
            {
                InvokeSetSecretOnImplementingType(name, secret, cmdlet);
            }
            else
            {
                InvokeSetSecretOnScriptFn(name, secret, cmdlet);
            }
        }

        /// <summary>
        /// Looks up a single secret by name.
        /// </summary>
        public object InvokeGetSecret(
            string name,
            PSCmdlet cmdlet)
        {
            if (!string.IsNullOrEmpty(this.ImplementingTypeName))
            {
                return InvokeGetSecretOnImplementingType(name, cmdlet);
            }
            else
            {
                return InvokeGetSecretOnScriptFn(name, cmdlet);
            }
        }

        /// <summary>
        /// Remove a single secret.
        /// </summary>
        public void InvokeRemoveSecret(
            string name,
            PSCmdlet cmdlet)
        {
            if (!string.IsNullOrEmpty(this.ImplementingTypeName))
            {
                InvokeRemoveSecretOnImplementingType(name, cmdlet);
            }
            else
            {
                InvokeRemoveSecretOnScriptFn(name, cmdlet);
            }
        }

        public KeyValuePair<string, string>[] InvokeGetSecretInfo(
            string filter,
            PSCmdlet cmdlet)
        {
            if (!string.IsNullOrEmpty(this.ImplementingTypeName))
            {
                return InvokeGetSecretInfoOnImplementingType(filter, cmdlet);
            }
            else
            {
                return InvokeGetSecretInfoOnScriptFn(filter, cmdlet);
            }
        }

        /// <summary>
        /// Creates copy of this extension module object instance.
        /// </summary>
        public ExtensionVaultModule Clone()
        {
            return new ExtensionVaultModule(this);
        }
        
        #endregion

        #region Implementing type implementation

        private void InvokeSetSecretOnImplementingType(
            string name,
            object secret,
            PSCmdlet cmdlet)
        {
            // Ensure the module has been imported so that the extension
            // binary assembly is loaded.
            ImportPSModule(cmdlet);

            var parameters = GetParamsFromStore(VaultParametersName);
            bool success = false;
            Exception error = null;

            try
            {
                success = _vaultExtentsion.Value.SetSecret(
                    name: name,
                    secret: secret,
                    parameters: parameters,
                    out error);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (!success || error != null)
            {
                if (error == null)
                {
                    var msg = string.Format(
                        CultureInfo.InvariantCulture, 
                        "Could not add secret {0} to vault {1}.",
                        name, VaultName);

                    error = new InvalidOperationException(msg);
                }

                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "InvokeSetSecretError",
                        ErrorCategory.InvalidOperation,
                        this));
            }
            else
            {
                cmdlet.WriteVerbose(
                    string.Format("Secret {0} was successfully added to vault {1}.", name, VaultName));
            }
        }

        private object InvokeGetSecretOnImplementingType(
            string name,
            PSCmdlet cmdlet)
        {
            // Ensure the module has been imported so that the extension
            // binary assembly is loaded.
            ImportPSModule(cmdlet);

            var parameters = GetParamsFromStore(VaultParametersName);
            object secret = null;
            Exception error = null;
            
            try
            {
                secret = _vaultExtentsion.Value.GetSecret(
                    name: name,
                    parameters: parameters,
                    out error);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (error != null)
            {
                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "InvokeGetSecretError",
                        ErrorCategory.InvalidOperation,
                        this));
            }

            return secret;
        }

        private void InvokeRemoveSecretOnImplementingType(
            string name,
            PSCmdlet cmdlet)
        {
            // Ensure the module has been imported so that the extension
            // binary assembly is loaded.
            ImportPSModule(cmdlet);

            var parameters = GetParamsFromStore(VaultParametersName);
            var success = false;
            Exception error = null;

            try
            {
                success = _vaultExtentsion.Value.RemoveSecret(
                    name: name,
                    parameters: parameters,
                    out error);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (!success || error != null)
            {
                if (error == null)
                {
                    var msg = string.Format(
                        CultureInfo.InvariantCulture, 
                        "Could not remove secret {0} from vault {1}.",
                        name, VaultName);

                    error = new InvalidOperationException(msg);
                }

                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "InvokeRemoveSecretError",
                        ErrorCategory.InvalidOperation,
                        this));
            }
            else
            {
                cmdlet.WriteVerbose(
                    string.Format("Secret {0} was successfully removed from vault {1}.", name, VaultName));
            }
        }

        private KeyValuePair<string, string>[] InvokeGetSecretInfoOnImplementingType(
            string filter,
            PSCmdlet cmdlet)
        {
            // Ensure the module has been imported so that the extension
            // binary assembly is loaded.
            ImportPSModule(cmdlet);

            var parameters = GetParamsFromStore(VaultParametersName);
            KeyValuePair<string, string>[] results = null;
            Exception error = null;

            try
            {
                results = _vaultExtentsion.Value.GetSecretInfo(
                    filter: filter,
                    parameters: parameters,
                    out error);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            
            if (error != null)
            {
                if (error == null)
                {
                    var msg = string.Format(
                        CultureInfo.InvariantCulture, 
                        "Could not get secret information from vault {0}.",
                        VaultName);

                    error = new InvalidOperationException(msg);
                }

                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "InvokeGetSecretInfoError",
                        ErrorCategory.InvalidOperation,
                        this));
            }

            return results;
        }

        #endregion

        #region Script function implementation

        private const string RunCommandScript = @"
            param (
                [string] $ModulePath,
                [string] $ModuleName,
                [string] $Command,
                [hashtable] $Params
            )
        
            Import-Module -Name $ModulePath
            & ""$ModuleName\$Command"" @Params
        ";

        private void InvokeSetSecretOnScriptFn(
            string name,
            object secret,
            PSCmdlet cmdlet)
        {
            var additionalParameters = GetAdditionalParams();
            var parameters = new Hashtable() {
                { "Name", name },
                { "Secret", secret },
                { "AdditionalParameters", additionalParameters }
            };

            var results = PowerShellInvoker.InvokeScript(
                script: RunCommandScript,
                args: new object[] { ModulePath, ModuleName, SetSecretCmd, parameters },
                error: out Exception error);

            bool success = results.Count > 0 ? (bool) results[0].BaseObject : false;
            
            if (!success || error != null)
            {
                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "errorId",
                        ErrorCategory.InvalidOperation,
                        this));
            }
            else
            {
                cmdlet.WriteVerbose(
                    string.Format("Secret {0} was successfully added to vault {1}.", name, VaultName));
            }
        }

        private object InvokeGetSecretOnScriptFn(
            string name,
            PSCmdlet cmdlet)
        {
            var additionalParameters = GetAdditionalParams();
            var parameters = new Hashtable() {
                { "Name", name },
                { "AdditionalParameters", additionalParameters }
            };

            var results = PowerShellInvoker.InvokeScript(
                script: RunCommandScript,
                args: new object[] { ModulePath, ModuleName, GetSecretCmd, parameters },
                error: out Exception error);
            
            if (error != null)
            {
                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "errorId",
                        ErrorCategory.InvalidOperation,
                        this));
            }
            
            return results.Count > 0 ? results[0].BaseObject : null;
        }

        private void InvokeRemoveSecretOnScriptFn(
            string name,
            PSCmdlet cmdlet)
        {
            var additionalParameters = GetAdditionalParams();
            var parameters = new Hashtable() {
                { "Name", name },
                { "AdditionalParameters", additionalParameters }
            };

            var results = PowerShellInvoker.InvokeScript(
                script: RunCommandScript,
                args: new object[] { ModulePath, ModuleName, RemoveSecretCmd, parameters },
                error: out Exception error);

            bool success = results.Count > 0 ? (bool) results[0].BaseObject : false;
            
            if (!success || error != null)
            {
                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "errorId",
                        ErrorCategory.InvalidOperation,
                        this));
            }
            else
            {
                cmdlet.WriteVerbose(
                    string.Format("Secret {0} was successfully removed from vault {1}.", name, VaultName));
            }
        }

        private KeyValuePair<string, string>[] InvokeGetSecretInfoOnScriptFn(
            string filter,
            PSCmdlet cmdlet)
        {
            var additionalParameters = GetAdditionalParams();
            var parameters = new Hashtable() {
                { "Filter", filter },
                { "AdditionalParameters", additionalParameters }
            };

            var results = PowerShellInvoker.InvokeScript(
                script: RunCommandScript,
                args: new object[] { ModulePath, ModuleName, GetSecretInfoCmd, parameters },
                error: out Exception error);
            
            if (error != null)
            {
                cmdlet.WriteError(
                    new ErrorRecord(
                        error,
                        "errorId",
                        ErrorCategory.InvalidOperation,
                        this));
            }

            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>(results.Count);
            foreach (dynamic item in results)
            {
                list.Add(
                    new KeyValuePair<string, string>(
                        key: item.Name,
                        value: item.Value));
            }

            return list.ToArray();
        }

        #endregion

        #region Private methods

        internal void ImportPSModule(PSCmdlet cmdlet)
        {
            cmdlet.InvokeCommand.InvokeScript(
                script: @"
                    param ([string] $ModulePath)

                    Import-Module -Name $ModulePath -Scope Local
                ",
                args: new object[] { this.ModulePath });
        }

        private Hashtable GetAdditionalParams()
        {
            if (!string.IsNullOrEmpty(VaultParametersName))
            {
                int errorCode = 0;
                if (LocalSecretStore.ReadObject(
                    name: VaultParametersName,
                    outObject: out object outObject,
                    ref errorCode))
                {
                    if (outObject is Hashtable hashtable)
                    {
                        return hashtable;
                    }
                }
            }

            return new Hashtable();
        }

        private static IReadOnlyDictionary<string, object> GetParamsFromStore(string paramsName)
        {
            if (!string.IsNullOrEmpty(paramsName))
            {
                int errorCode = 0;
                if (LocalSecretStore.ReadObject(
                    paramsName,
                    out object outObject,
                    ref errorCode))
                {
                    var hashtable = outObject as Hashtable;
                    var dictionary = new Dictionary<string, object>(hashtable.Count);
                    foreach (var key in hashtable.Keys)
                    {
                        dictionary.Add((string) key, hashtable[key]);
                    }
                    return new ReadOnlyDictionary<string, object>(dictionary);
                }
            }

            return null;
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
                        // TODO: Think about using thread local storage so that we can re-use an instance on the same thread.
                        returnVaults.Add(vaultName, _vaultCache[vaultName].Clone());
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
            _registryWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
            _registryWatcher.Filter = "VaultInfo";
            _registryWatcher.EnableRaisingEvents = true;
            _registryWatcher.Changed += (sender, args) => { if (_allowAutoRefresh) { RefreshCache(); } };
            _registryWatcher.Created += (sender, args) => { if (_allowAutoRefresh) { RefreshCache(); } };
            _registryWatcher.Deleted += (sender, args) => { if (_allowAutoRefresh) { RefreshCache(); } };

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

            try
            {
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
            catch (Exception)
            {
                // If an exception is thrown while parsing the registry file, assume the file is corrupted and delete it.
                DeleteSecretVaultRegistryFile();
            }
        }

        private static Hashtable ConvertJsonToHashtable(string json)
        {
            var psObject = PowerShellInvoker.InvokeScript(
                script: ConvertJsonToHashtableScript,
                args: new object[] { json },
                error: out Exception _);

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

            return new Hashtable();
        }

        private static void DeleteSecretVaultRegistryFile()
        {
            try
            {
                File.Delete(RegistryFilePath);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Writes the Hashtable registered vault information data to file as json.
        /// </summary>
        /// <param>Hashtable containing registered vault information.</param>
        private static void WriteSecretVaultRegistry(Hashtable dataToWrite)
        {
            var psObject = PowerShellInvoker.InvokeScript(
                script: @"param ([hashtable] $dataToWrite) ConvertTo-Json $dataToWrite",
                args: new object[] { dataToWrite },
                error: out Exception _);
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

    #region PowerShellInvoker

    internal static class PowerShellInvoker
    {
        #region Members

        // Ensure there is one instance of PowerShell per thread by using [ThreadStatic]
        // attribute to store each local thread instance.
        [ThreadStatic]
        private static System.Management.Automation.PowerShell _powerShell;

        #endregion

        #region Constructor

        static PowerShellInvoker()
        {
            _powerShell = System.Management.Automation.PowerShell.Create();
        }

        #endregion

        #region Methods

        private static void CheckPowerShell()
        {
            if ((_powerShell.InvocationStateInfo.State != PSInvocationState.Completed && _powerShell.InvocationStateInfo.State != PSInvocationState.NotStarted)
                || (_powerShell.Runspace.RunspaceStateInfo.State != RunspaceState.Opened))
            {
                _powerShell.Dispose();
                _powerShell = System.Management.Automation.PowerShell.Create();

                _powerShell = System.Management.Automation.PowerShell.Create();
                return;
            }

            _powerShell.Commands.Clear();
            _powerShell.Streams.ClearStreams();
            _powerShell.Runspace.ResetRunspaceState();
        }

        public static Collection<PSObject> InvokeScript(
            string script,
            object[] args,
            out Exception error)
        {
            CheckPowerShell();

            error = null;
            Collection<PSObject> results;
            try
            {
                results = _powerShell.AddScript(script).AddParameters(args).Invoke();
                if (_powerShell.Streams.Error.Count > 0)
                {
                    error = _powerShell.Streams.Error[0].Exception;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                results = new Collection<PSObject>();
            }

            return results;
        }
    }

    #endregion
}

    #endregion
