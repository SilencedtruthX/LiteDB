﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LiteDB
{
    public partial class LiteCollection<T>
    {
        /// <summary>
        /// Update a document in this collection. Returns false if not found document in collection
        /// </summary>
        public virtual bool Update(T document)
        {
            if (document == null) throw new ArgumentNullException("document");

            // get BsonDocument from object
            var doc = this.Database.Mapper.ToDocument(document);

            if (doc.Id == null || doc.Id.IsNull) throw new LiteException("Document Id can't be null");

            // serialize object
            var bytes = BsonSerializer.Serialize(doc);

            // start transaction
            this.Database.Transaction.Begin();

            try
            {
                var col = this.GetCollectionPage(false);

                // if no collection, no updates
                if (col == null)
                {
                    this.Database.Transaction.Abort();
                    return false;
                }

                // find indexNode from pk index
                var indexNode = this.Database.Indexer.FindOne(col.PK, doc.Id.RawValue);

                // if not found document, no updates
                if (indexNode == null)
                {
                    this.Database.Transaction.Abort();
                    return false;
                }

                // update data storage
                var dataBlock = this.Database.Data.Update(col, indexNode.DataBlock, bytes);

                // delete/insert indexes - do not touch on PK
                foreach (var index in col.GetIndexes(false))
                {
                    var key = doc.Get(index.Field);

                    var node = this.Database.Indexer.GetNode(dataBlock.IndexRef[index.Slot]);

                    // check if my index node was changed
                    if (node.Key.CompareTo(new IndexKey(key)) != 0)
                    {
                        // remove old index node
                        this.Database.Indexer.Delete(index, node.Position);

                        // and add a new one
                        var newNode = this.Database.Indexer.AddNode(index, key);

                        // point my index to data object
                        newNode.DataBlock = dataBlock.Position;

                        // point my dataBlock
                        dataBlock.IndexRef[index.Slot] = newNode.Position;

                        dataBlock.Page.IsDirty = true;
                    }
                }

                this.Database.Transaction.Commit();

                return true;
            }
            catch
            {
                this.Database.Transaction.Rollback();
                throw;
            }
        }
    }
}
