﻿using Raven.Abstractions;
using Raven.Abstractions.Connection;
using Raven.Abstractions.Data;
using Raven.Abstractions.Exceptions;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Logging;
using Raven.Abstractions.MEF;
using Raven.Abstractions.Util;
using Raven.Abstractions.Util.Streams;
using Raven.Database.Impl;
using Raven.Database.Plugins;
using Raven.Database.Storage.Voron.Impl;
using Raven.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Raven.Bundles.Compression.Plugin;
using Voron;
using Voron.Impl;
using Constants = Raven.Abstractions.Data.Constants;

namespace Raven.Database.Storage.Voron.StorageActions
{
	internal class DocumentsStorageActions : StorageActionsBase, IDocumentStorageActions
	{
		private readonly Reference<WriteBatch> writeBatch;

		private readonly IUuidGenerator uuidGenerator;
		private readonly OrderedPartCollection<AbstractDocumentCodec> documentCodecs;
		private readonly IDocumentCacher documentCacher;

		private static readonly ILog logger = LogManager.GetCurrentClassLogger();
		private readonly Dictionary<Etag, Etag> etagTouches = new Dictionary<Etag, Etag>();
		private readonly TableStorage tableStorage;

		private readonly Index metadataIndex;

		public DocumentsStorageActions(IUuidGenerator uuidGenerator,
			OrderedPartCollection<AbstractDocumentCodec> documentCodecs,
			IDocumentCacher documentCacher,
			Reference<WriteBatch> writeBatch,
			Reference<SnapshotReader> snapshot,
			TableStorage tableStorage,
			IBufferPool bufferPool)
			: base(snapshot, bufferPool)
		{
			this.uuidGenerator = uuidGenerator;
			this.documentCodecs = documentCodecs;
			this.documentCacher = documentCacher;
			this.writeBatch = writeBatch;
			this.tableStorage = tableStorage;

			metadataIndex = tableStorage.Documents.GetIndex(Tables.Documents.Indices.Metadata);
		}

		public IEnumerable<JsonDocument> GetDocumentsByReverseUpdateOrder(int start, int take)
		{
			if (start < 0)
				throw new ArgumentException("must have zero or positive value", "start");
			if (take < 0)
				throw new ArgumentException("must have zero or positive value", "take");
			if (take == 0) yield break;

			using (var iterator = tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag)
											.Iterate(Snapshot, writeBatch.Value))
			{
				int fetchedDocumentCount = 0;
				if (!iterator.Seek(Slice.AfterAllKeys))
					yield break;

				if (!iterator.Skip(-start))
					yield break;
				do
				{
					if (iterator.CurrentKey == null || iterator.CurrentKey.Equals(Slice.Empty))
						yield break;

					var key = GetKeyFromCurrent(iterator);

					var document = DocumentByKey(key);
					if (document == null) //precaution - should never be true
					{
						throw new InvalidDataException(string.Format("Possible data corruption - the key = '{0}' was found in the documents index, but matching document was not found.", key));
					}

					yield return document;

					fetchedDocumentCount++;
				} while (iterator.MovePrev() && fetchedDocumentCount < take);
			}
		}

        public IEnumerable<JsonDocument> GetDocumentsAfterWithIdStartingWith(Etag etag, string idPrefix, int take, CancellationToken cancellationToken, long? maxSize = null, Etag untilEtag = null, TimeSpan? timeout = null, Action<Etag> lastProcessedDocument = null)
        {
            if (take < 0)
                throw new ArgumentException("must have zero or positive value", "take");

            if (take == 0) 
                yield break;

            if (string.IsNullOrEmpty(etag))
                throw new ArgumentNullException("etag");

            Stopwatch duration = null;
            if (timeout != null)
                duration = Stopwatch.StartNew();


            Etag lastDocEtag = null;
            using (var iterator = tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag)
                                              .Iterate(Snapshot, writeBatch.Value))
            {
                var slice = (Slice)etag.ToString();
                if (iterator.Seek(slice) == false)
                    yield break;

                if (iterator.CurrentKey.Equals(slice)) // need gt, not ge
                {
                    if (iterator.MoveNext() == false)
                        yield break;
                }

                long fetchedDocumentTotalSize = 0;
                int fetchedDocumentCount = 0;

                Etag docEtag = etag;

                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    docEtag = Etag.Parse(iterator.CurrentKey.ToString());

                    // We can skip many documents so the timeout should be at the start of the process to be executed.
                    if (timeout != null)
                    {
                        if (duration.Elapsed > timeout.Value)
                            break;
                    }     

                    if (untilEtag != null)
                    {
                        // This is not a failure, we are just ahead of when we expected to. 
                        if (EtagUtil.IsGreaterThan(docEtag, untilEtag))
                            break;
                    }

                    var key = GetKeyFromCurrent(iterator);
                    if (!string.IsNullOrEmpty(idPrefix))
                    {                        
                        if (!key.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            // We assume that we have processed it because it is not of our interest.
                            lastDocEtag = docEtag;
                            continue;
                        }                            
                    }                        

                    var document = DocumentByKey(key);
                    if (document == null) //precaution - should never be true
                    {
                        throw new InvalidDataException(string.Format("Data corruption - the key = '{0}' was found in the documents index, but matching document was not found", key));
                    }

                    if (!document.Etag.Equals(docEtag))
                    {
                        throw new InvalidDataException(string.Format("Data corruption - the etag for key ='{0}' is different between document and its index", key));
                    }

                    fetchedDocumentTotalSize += document.SerializedSizeOnDisk;
                    fetchedDocumentCount++;

                    yield return document;

                    lastDocEtag = docEtag;

                    if (maxSize.HasValue && fetchedDocumentTotalSize >= maxSize)
                        break;
                } 
                while (iterator.MoveNext() && fetchedDocumentCount < take);
            }

            // We notify the last that we considered.
            if (lastProcessedDocument != null)
                lastProcessedDocument(lastDocEtag);
        }

        public IEnumerable<JsonDocument> GetDocumentsAfter(Etag etag, int take, CancellationToken cancellationToken, long? maxSize = null, Etag untilEtag = null, TimeSpan? timeout = null, Action<Etag> lastProcessedOnFailure = null)
        {
            return GetDocumentsAfterWithIdStartingWith(etag, null, take, cancellationToken, maxSize, untilEtag, timeout, lastProcessedOnFailure);
        }

		private static string GetKeyFromCurrent(global::Voron.Trees.IIterator iterator)
		{
			string key;
			using (var currentDataStream = iterator.CreateReaderForCurrent().AsStream())
			{
				var keyBytes = currentDataStream.ReadData();
				key = Encoding.UTF8.GetString(keyBytes);
			}
			return key;
		}

		public IEnumerable<JsonDocument> GetDocumentsWithIdStartingWith(string idPrefix, int start, int take, string skipAfter)
		{
			if (string.IsNullOrEmpty(idPrefix))
				throw new ArgumentNullException("idPrefix");
			if (start < 0)
				throw new ArgumentException("must have zero or positive value", "start");
			if (take < 0)
				throw new ArgumentException("must have zero or positive value", "take");

			if (take == 0)
				yield break;

			using (var iterator = tableStorage.Documents.Iterate(Snapshot, writeBatch.Value))
			{
                iterator.RequiredPrefix = (Slice)idPrefix.ToLowerInvariant();
                var seekStart = skipAfter == null ? (Slice)iterator.RequiredPrefix : (Slice)skipAfter.ToLowerInvariant();
				if (iterator.Seek(seekStart) == false || !iterator.Skip(start))
					yield break;

				if (skipAfter != null && !iterator.MoveNext())
					yield break; // move to the _next_ one


				var fetchedDocumentCount = 0;
				do
				{
					var key = iterator.CurrentKey.ToString();

					var fetchedDocument = DocumentByKey(key);
					if (fetchedDocument == null) continue;

					fetchedDocumentCount++;
					yield return fetchedDocument;
				} while (iterator.MoveNext() && fetchedDocumentCount < take);
			}
		}

		public long GetDocumentsCount()
		{
			return tableStorage.GetEntriesCount(tableStorage.Documents);
		}

		public Stream RawDocumentByKey(string key)
		{
            var normalizedKey = (Slice)CreateKey(key);

			var documentReadResult = tableStorage.Documents.Read(Snapshot, normalizedKey, writeBatch.Value);
			if (documentReadResult == null) //non existing document
				return null;

			return documentReadResult.Reader.AsStream();
		}

		public JsonDocument DocumentByKey(string key)
		{
			if (string.IsNullOrEmpty(key))
			{
				logger.Debug("Document with empty key was not found");
				return null;
			}

            var normalizedKey = CreateKey(key);
            var sliceKey = (Slice)normalizedKey;

            var metadataDocument = ReadDocumentMetadata(normalizedKey, sliceKey);
			if (metadataDocument == null)
			{
				logger.Debug("Document with key='{0}' was not found", key);
				return null;
			}

			int sizeOnDisk;
            var documentData = ReadDocumentData(normalizedKey, sliceKey, metadataDocument.Etag, metadataDocument.Metadata, out sizeOnDisk);
			if (documentData == null)
			{
				logger.Warn("Could not find data for {0}, but found the metadata", key);
				return null;
			}

            var metadataSize = metadataIndex.GetDataSize(Snapshot, sliceKey);

			return new JsonDocument
			{
				DataAsJson = documentData,
				Etag = metadataDocument.Etag,
				Key = metadataDocument.Key, //original key - with user specified casing, etc.
				Metadata = metadataDocument.Metadata,
				SerializedSizeOnDisk = sizeOnDisk + metadataSize,
				LastModified = metadataDocument.LastModified
			};
		}

		public JsonDocumentMetadata DocumentMetadataByKey(string key)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

            var normalizedKey = CreateKey(key);
            var sliceKey = (Slice)normalizedKey;

            if (tableStorage.Documents.Contains(Snapshot, sliceKey, writeBatch.Value))
                return ReadDocumentMetadata(normalizedKey, sliceKey);

			logger.Debug("Document with key='{0}' was not found", key);
			return null;
		}

		public bool DeleteDocument(string key, Etag etag, out RavenJObject metadata, out Etag deletedETag)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

            var normalizedKey = CreateKey(key);
            var normalizedKeyAsSlice = (Slice)normalizedKey;

			if (etag != null)
				EnsureDocumentEtagMatch(normalizedKey, etag, "DELETE");

			ushort? existingVersion;
            if (!tableStorage.Documents.Contains(Snapshot, normalizedKeyAsSlice, writeBatch.Value, out existingVersion))
			{
				logger.Debug("Document with key '{0}' was not found, and considered deleted", key);
				metadata = null;
				deletedETag = null;
				return false;
			}

            if (!metadataIndex.Contains(Snapshot, normalizedKeyAsSlice, writeBatch.Value)) //data exists, but metadata is not --> precaution, should never be true
			{
				var errorString = string.Format("Document with key '{0}' was found, but its metadata wasn't found --> possible data corruption", key);
				throw new InvalidDataException(errorString);
			}

            var existingEtag = EnsureDocumentEtagMatch(normalizedKey, etag, "DELETE");
            var documentMetadata = ReadDocumentMetadata(normalizedKey, normalizedKeyAsSlice);
			metadata = documentMetadata.Metadata;

			deletedETag = etag != null ? existingEtag : documentMetadata.Etag;

			tableStorage.Documents.Delete(writeBatch.Value, normalizedKey, existingVersion);
			metadataIndex.Delete(writeBatch.Value, normalizedKey);

			tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag)
						  .Delete(writeBatch.Value, deletedETag);

			documentCacher.RemoveCachedDocument(normalizedKey, etag);

			logger.Debug("Deleted document with key = '{0}'", key);

			return true;
		}

		public AddDocumentResult AddDocument(string key, Etag etag, RavenJObject data, RavenJObject metadata)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

			if (key != null && Encoding.UTF8.GetByteCount(key) >= UInt16.MaxValue)
				throw new ArgumentException(string.Format("The dataKey must be a maximum of {0} bytes in Unicode, key is: '{1}'", UInt16.MaxValue, key), "key");

			Etag existingEtag;
			Etag newEtag;

			DateTime savedAt;
            var normalizedKey = CreateKey(key);
            var isUpdate = WriteDocumentData(key, normalizedKey, etag, data, metadata, out newEtag, out existingEtag, out savedAt);

			logger.Debug("AddDocument() - {0} document with key = '{1}'", isUpdate ? "Updated" : "Added", key);

			return new AddDocumentResult
			{
				Etag = newEtag,
				PrevEtag = existingEtag,
				SavedAt = savedAt,
				Updated = isUpdate
			};
		}

        private bool PutDocumentMetadataInternal(string key, Slice normalizedKey, RavenJObject metadata, Etag newEtag, DateTime savedAt)
		{
			return WriteDocumentMetadata(new JsonDocumentMetadata
			{
				Key = key,
				Etag = newEtag,
				Metadata = metadata,
				LastModified = savedAt
            }, normalizedKey);
		}

		public void IncrementDocumentCount(int value)
		{
			//nothing to do here			
		}

		public AddDocumentResult InsertDocument(string key, RavenJObject data, RavenJObject metadata, bool overwriteExisting)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

            if (!overwriteExisting && tableStorage.Documents.Contains(Snapshot, (Slice)CreateKey(key), writeBatch.Value))
			{
				throw new ConcurrencyException(string.Format("InsertDocument() - overwriteExisting is false and document with key = '{0}' already exists", key));
			}

			return AddDocument(key, null, data, metadata);
		}

		public void TouchDocument(string key, out Etag preTouchEtag, out Etag afterTouchEtag)
		{
			if (string.IsNullOrEmpty(key))
				throw new ArgumentNullException("key");

            var normalizedKey = CreateKey(key);
            var normalizedKeySlice = (Slice)normalizedKey;

            if (!tableStorage.Documents.Contains(Snapshot, normalizedKeySlice, writeBatch.Value))
			{
				logger.Debug("Document with dataKey='{0}' was not found", key);
				preTouchEtag = null;
				afterTouchEtag = null;
				return;
			}

            var metadata = ReadDocumentMetadata(normalizedKey, normalizedKeySlice);

			var newEtag = uuidGenerator.CreateSequentialUuid(UuidType.Documents);
			afterTouchEtag = newEtag;
			preTouchEtag = metadata.Etag;
			metadata.Etag = newEtag;

            WriteDocumentMetadata(metadata, normalizedKeySlice, shouldIgnoreConcurrencyExceptions: true);

			var keyByEtagIndex = tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag);

			keyByEtagIndex.Delete(writeBatch.Value, preTouchEtag);
			keyByEtagIndex.Add(writeBatch.Value, newEtag, normalizedKey);
			etagTouches.Add(preTouchEtag, afterTouchEtag);

			logger.Debug("TouchDocument() - document with key = '{0}'", key);
		}

		public Etag GetBestNextDocumentEtag(Etag etag)
		{
			if (etag == null) throw new ArgumentNullException("etag");

			using (var iter = tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag)
													.Iterate(Snapshot, writeBatch.Value))
			{
                if (!iter.Seek((Slice)etag.ToString()) &&
					!iter.Seek(Slice.BeforeAllKeys)) //if parameter etag not found, scan from beginning. if empty --> return original etag
					return etag;

				do
				{
					var docEtag = Etag.Parse(iter.CurrentKey.ToString());

					if (EtagUtil.IsGreaterThan(docEtag, etag))
						return docEtag;
				} while (iter.MoveNext());
			}

			return etag; //if not found, return the original etag
		}

		private Etag EnsureDocumentEtagMatch(string key, Etag etag, string method)
		{
            var sliceKey = (Slice)key;

			var metadata = ReadDocumentMetadata(key, sliceKey);
			if (metadata == null)
				return Etag.InvalidEtag;

			var existingEtag = metadata.Etag;


			if (etag != null)
			{
				Etag next;
				while (etagTouches.TryGetValue(etag, out next))
				{
					etag = next;
				}

				if (existingEtag != etag)
				{
					if (etag == Etag.Empty)
					{
						if (metadata.Metadata.ContainsKey(Constants.RavenDeleteMarker) &&
							metadata.Metadata.Value<bool>(Constants.RavenDeleteMarker))
						{
							return existingEtag;
						}
					}

					throw new ConcurrencyException(method + " attempted on document '" + key +
												   "' using a non current etag")
					{
						ActualETag = existingEtag,
						ExpectedETag = etag
					};
				}
			}

			return existingEtag;
		}

		//returns true if it was update operation
		private bool WriteDocumentMetadata(JsonDocumentMetadata metadata, Slice key, bool shouldIgnoreConcurrencyExceptions = false)
		{
			var metadataStream = CreateStream();

			metadataStream.Write(metadata.Etag);
			metadataStream.Write(metadata.Key);

			if (metadata.LastModified.HasValue)
				metadataStream.Write(metadata.LastModified.Value.ToBinary());
			else
				metadataStream.Write((long)0);

			metadata.Metadata.WriteTo(metadataStream);

			metadataStream.Position = 0;

			ushort? existingVersion;
			var isUpdate = metadataIndex.Contains(Snapshot, key, writeBatch.Value, out existingVersion);
			metadataIndex.Add(writeBatch.Value, key, metadataStream, existingVersion, shouldIgnoreConcurrencyExceptions);

			return isUpdate;
		}

        private JsonDocumentMetadata ReadDocumentMetadata(string normalizedKey, Slice sliceKey)
		{
			try
			{
                var metadataReadResult = metadataIndex.Read(Snapshot, sliceKey, writeBatch.Value);
				if (metadataReadResult == null)
					return null;

				using (var stream = metadataReadResult.Reader.AsStream())
				{
					stream.Position = 0;
					var etag = stream.ReadEtag();
					var originalKey = stream.ReadString();
					var lastModifiedDateTimeBinary = stream.ReadInt64();

					var existingCachedDocument = documentCacher.GetCachedDocument(normalizedKey, etag);

					var metadata = existingCachedDocument != null ? existingCachedDocument.Metadata : stream.ToJObject();
					var lastModified = DateTime.FromBinary(lastModifiedDateTimeBinary);

					return new JsonDocumentMetadata
					{
						Key = originalKey,
						Etag = etag,
						Metadata = metadata,
						LastModified = lastModified
					};
				}
			}
			catch (Exception e)
			{

                throw new InvalidDataException("Failed to de-serialize metadata of document " + normalizedKey, e);
			}
		}

		private bool WriteDocumentData(string key, string normalizedKey, Etag etag, RavenJObject data, RavenJObject metadata, out Etag newEtag, out Etag existingEtag, out DateTime savedAt)
		{
            var normalizedKeySlice = (Slice)normalizedKey;
			var keyByEtagDocumentIndex = tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag);

			ushort? existingVersion;
            var isUpdate = tableStorage.Documents.Contains(Snapshot, normalizedKeySlice, writeBatch.Value, out existingVersion);
			existingEtag = null;

			if (isUpdate)
			{
				existingEtag = EnsureDocumentEtagMatch(normalizedKey, etag, "PUT");
				keyByEtagDocumentIndex.Delete(writeBatch.Value, existingEtag);
			}
			else if (etag != null && etag != Etag.Empty)
			{
				throw new ConcurrencyException("PUT attempted on document '" + key + "' using a non current etag (document deleted)")
				{
					ExpectedETag = etag
				};
			}

			var dataStream = CreateStream();

			using (var finalDataStream = documentCodecs.Aggregate((Stream)new UndisposableStream(dataStream),
				(current, codec) => codec.Encode(normalizedKey, data, metadata, current)))
			{
				data.WriteTo(finalDataStream);
				finalDataStream.Flush();
			}

			dataStream.Position = 0;
            tableStorage.Documents.Add(writeBatch.Value, normalizedKeySlice, dataStream, existingVersion ?? 0);

			newEtag = uuidGenerator.CreateSequentialUuid(UuidType.Documents);
			savedAt = SystemTime.UtcNow;

            var isUpdated = PutDocumentMetadataInternal(key, normalizedKeySlice, metadata, newEtag, savedAt);

			keyByEtagDocumentIndex.Add(writeBatch.Value, newEtag, normalizedKey);

			return isUpdated;
		}

        private RavenJObject ReadDocumentData(string normalizedKey, Slice sliceKey, Etag existingEtag, RavenJObject metadata, out int size)
		{
			try
			{
				size = -1;

				var existingCachedDocument = documentCacher.GetCachedDocument(normalizedKey, existingEtag);
				if (existingCachedDocument != null)
				{
					size = existingCachedDocument.Size;
					return existingCachedDocument.Document;
				}

                var documentReadResult = tableStorage.Documents.Read(Snapshot, sliceKey, writeBatch.Value);
				if (documentReadResult == null) //non existing document
					return null;

				using (var stream = documentReadResult.Reader.AsStream())
				{
					using (var decodedDocumentStream = documentCodecs.Aggregate(stream,
							(current, codec) => codec.Value.Decode(normalizedKey, metadata, current)))
					{
						var streamToUse = decodedDocumentStream;
						if (stream != decodedDocumentStream)
							streamToUse = new CountingStream(decodedDocumentStream);

						var documentData = decodedDocumentStream.ToJObject();

						size = (int)Math.Max(stream.Position, streamToUse.Position);
						documentCacher.SetCachedDocument(normalizedKey, existingEtag, documentData, metadata, size);

						return documentData;
					}
				}
			}
			catch (Exception e)
			{ 
                InvalidDataException invalidDataException = null;
			    try
			    {
                    size = -1;
                    var documentReadResult = tableStorage.Documents.Read(Snapshot, sliceKey, writeBatch.Value);
                    if (documentReadResult == null) //non existing document
                        return null;

                    using (var stream = documentReadResult.Reader.AsStream())
                    {
			            using (var reader = new BinaryReader(stream))
			            {
			                if (reader.ReadUInt32() == DocumentCompression.CompressFileMagic)
			                {
			                    invalidDataException = new InvalidDataException(string.Format("Document '{0}' is compressed, but the compression bundle is not enabled.\r\n" +
                                                                                              "You have to enable the compression bundle when dealing with compressed documents.", normalizedKey), e);
			                }
			            }
			        }

			
			    }
			    catch (Exception)
			    {
			        // we are already in error handling mode, just ignore this
			    }
			    if(invalidDataException != null)
                    throw invalidDataException;

                throw new InvalidDataException("Failed to de-serialize a document: " + normalizedKey, e);
			}
		}

		public DebugDocumentStats GetDocumentStatsVerySlowly()
		{
			var sp = Stopwatch.StartNew();
			var stat = new DebugDocumentStats { Total = GetDocumentsCount() };

			var documentsByEtag = tableStorage.Documents.GetIndex(Tables.Documents.Indices.KeyByEtag);
			using (var iterator = documentsByEtag.Iterate(Snapshot, writeBatch.Value))
			{
				if (!iterator.Seek(Slice.BeforeAllKeys))
				{
					stat.TimeToGenerate = sp.Elapsed;
					return stat;
				}

				do
				{
					var key = GetKeyFromCurrent(iterator);
					var doc = DocumentByKey(key);
					var size = doc.SerializedSizeOnDisk;
					stat.TotalSize += size;
					if (key.StartsWith("Raven/", StringComparison.OrdinalIgnoreCase))
					{
						stat.System.Update(size, doc.Key);
					}

                    var normalizedKey = CreateKey(key);
                    var metadata = ReadDocumentMetadata(normalizedKey, (Slice) key);

					var entityName = metadata.Metadata.Value<string>(Constants.RavenEntityName);
					if (string.IsNullOrEmpty(entityName))
					{
						stat.NoCollection.Update(size, doc.Key);
					}
					else
					{
						stat.IncrementCollection(entityName, size, doc.Key);
					}

					if (metadata.Metadata.ContainsKey(Constants.RavenDeleteMarker))
						stat.Tombstones++;

				}
				while (iterator.MoveNext());
				stat.TimeToGenerate = sp.Elapsed;
				return stat;
			}
		}
    }
}
