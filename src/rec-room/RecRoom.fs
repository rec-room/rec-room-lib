namespace RecRoom

open System.Net
open System
module Client =
    open System.Linq
    open Microsoft.Azure.Documents.Client
    open Microsoft.Azure.Documents


    type DbId = string
    type CollId = string
    type RecId = string
    type RecType = string
    type PartitionKey = string

    type DBErrors =
    | RecordExists
    | RecordAbsent


    type DbContext = {
        Endpoint : Uri
        AccountKey : string
        ConnectionPolicy : ConnectionPolicy
        Id : DbId
        Client : DocumentClient
    } with
        member internal this.DbUri = UriFactory.CreateDatabaseUri(this.Id.ToString())
        member internal this.CollectionContext collId = {CollectionId = collId; DbContext = this}
    and CollectionContext = {
        CollectionId : CollId
        DbContext : DbContext
    } with
        member internal this.CollUri =  UriFactory.CreateDocumentCollectionUri(this.DbContext.Id.ToString(), this.CollectionId.ToString())

    let private docUri collCntxt recId = UriFactory.CreateDocumentUri(collCntxt.DbContext.Id.ToString(), collCntxt.CollectionId.ToString(), recId.ToString())


    // DATABASE OPERATIONS
    //

    let ensureDb dbCntxt =
        let db = Database()
        db.Id <- (dbCntxt.Id.ToString())
        (dbCntxt.Client.CreateDatabaseIfNotExistsAsync(db)).Wait()

    let createDb dbCntxt =
        let db = Database()
        db.Id <- (dbCntxt.Id.ToString())
        (dbCntxt.Client.CreateDatabaseAsync(db)).Wait()
        dbCntxt.Id

    let tryCreateDb dbCntxt =
        try
            createDb dbCntxt  |> Some
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.Conflict)
                    then None else reraise()
                | _ -> reraise()

    /// Return a context for accessing a database. If create is true then create a database if the id does not exist.
    let dbContext (endpoint: Uri) (accountKey: string) dbName createIfAbsent =
        let connPolicy = ConnectionPolicy()
        connPolicy.ConnectionMode <- ConnectionMode.Direct
        connPolicy.ConnectionProtocol <- Protocol.Tcp

        let dbClient  = new DocumentClient (endpoint, accountKey, connPolicy)
        dbClient.OpenAsync().Wait()
        let cntxt = {
            Endpoint = endpoint
            AccountKey = accountKey
            ConnectionPolicy = connPolicy
            Id = dbName
            Client = dbClient}
        if createIfAbsent then ensureDb cntxt
        cntxt




    // OPERATIONS ON COLLECTIONS
    //

    /// Create a collection with the given id. Fail if there is already a collection with the given id.
    let createCollection collCntxt throughPut =
        let coll = DocumentCollection()
        coll.Id <- (collCntxt.CollectionId.ToString())
        let options = RequestOptions()
        options.OfferThroughput <- Nullable throughPut
        let resp = (collCntxt.DbContext.Client.CreateDocumentCollectionAsync (collCntxt.DbContext.DbUri, coll, options)).Result
        resp.Resource.Id

    /// Try create a collection with the given id. Return None if the collection id already exists.
    let tryCreateCollection collCntxt throughPut =
        try
            createCollection collCntxt throughPut |> Some
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.Conflict)
                    then None else reraise()
                | _ -> reraise()

    /// Create a collection of one with the collection id does not already exist.
    let ensureCollection collCntxt throughPut =
        let coll = DocumentCollection()
        coll.Id <- (collCntxt.CollectionId.ToString())
        let options = RequestOptions()
        options.OfferThroughput <- Nullable throughPut
        (collCntxt.DbContext.Client.CreateDocumentCollectionIfNotExistsAsync (collCntxt.DbContext.DbUri, coll, options)).Wait()

    /// Return all collections in the database.
    let collections dbCntxt=
        dbCntxt.Client.CreateDocumentCollectionQuery(dbCntxt.DbUri).AsEnumerable()
        |> Seq.map (fun c -> dbCntxt.CollectionContext c.Id )

    /// Try to delete a collection. Return None if there is no collection with the given id.
    let tryDeleteCollection collCntxt =
        try
            (collCntxt.DbContext.Client.DeleteDocumentCollectionAsync(collCntxt.CollUri)).Wait()
            Some collCntxt.CollectionId
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.NotFound)
                    then None else reraise()
                | _ -> reraise()

    /// Delete all collections in the database.
    let deleteCollections dbContext =
        collections dbContext
        |> Seq.map (fun c -> tryDeleteCollection c)

    let collectionContext dbCntxt collId throughPut createIfAbsent =
        let cntxt = {DbContext = dbCntxt; CollectionId = collId}
        if createIfAbsent then ensureCollection cntxt throughPut
        cntxt


    // OPERATIONS ON RECORDS
    //

    /// Insert a record. Will fail if a record with the id already exists. If
    /// it is possible the record already exists use 'upsert' or 'tryInsert'
    let insert collCntxt record =
        let resp = (collCntxt.DbContext.Client.CreateDocumentAsync(collCntxt.CollUri, record, null, true)).Result
        resp.Resource.Id

    /// Insert a record. The record should have an "id" property. Will return None if the id already exists.
    let tryInsert collCntxt record =
        try
            insert collCntxt record |> Some
        with
        | :? System.AggregateException as ex ->
            match ex.GetBaseException() with
            | :? DocumentClientException as e when e.StatusCode = Nullable(HttpStatusCode.Conflict) -> None
            | _ -> reraise()

    /// Replace (AKA update the given record.) Will fail if there is no record with the given id.
    let replace collCntxt record =
        // This is not less 'safe' because ALL CosmosDB records must have an id propery
        let propInfo = record.GetType().GetProperty("id")
        let recId = propInfo.GetValue(record, null)
        let uri = docUri collCntxt recId
        (collCntxt.DbContext.Client.ReplaceDocumentAsync(uri, record, null)).Wait()

    /// Upsert a record. This is either an insert if the record does not exist or a replace if it does.
    let upsert collCntxt record =
        (collCntxt.DbContext.Client.UpsertDocumentAsync(collCntxt.CollUri, record, null, true)).Wait()

    /// Get a record using the id property. Fail if the record does not exist.
    let read<'T> collCntxt recId : 'T =
        let uri = docUri collCntxt recId
        (collCntxt.DbContext.Client.ReadDocumentAsync<'T>(uri, null)).Result.Document

    /// Try get a record using the id property. Return None if the record does not exist.
    let tryRead<'T> collCntxt recId : 'T option =
        try
            read<'T> collCntxt recId |> Some
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.NotFound)
                    then None else reraise()
                | _ -> reraise()

    /// Delete a record using the id property. Fail if the record does not exist.
    let delete collCntxt recId =
        let uri = docUri collCntxt recId
        (collCntxt.DbContext.Client.DeleteDocumentAsync(uri, null)).Result |> ignore
        recId

    /// Try Delete a record using the id property. Return None if the record did not exist.
    let tryDelete collCntxt recId =
        try
            delete collCntxt recId |> Some
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.NotFound)
                    then None else reraise()
                | _ -> reraise()

