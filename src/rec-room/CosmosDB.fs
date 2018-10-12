namespace RecRoom

open System.Data.SqlClient
module Client2 =

    open SerializationUtil
    open Microsoft.Azure.Documents
    open Microsoft.Azure.Documents.Client
    open System
    open System.Net
    open System.Linq



    let conPolicy = ConnectionPolicy()
    conPolicy.ConnectionMode <- ConnectionMode.Direct
    conPolicy.ConnectionProtocol <- Protocol.Tcp

    let serializerSettings = new Newtonsoft.Json.JsonSerializerSettings()

    let dbClient  =
        new DocumentClient(Uri("https://localhost:8081"), "accountKey", conPolicy)
    dbClient.OpenAsync().Wait()

    // The account will have been created by the template. Initialize
    // the database and the single collection private to the Seymour system
    let dbName = "foo"
    let private db = Database()
    db.Id <- dbName
    (dbClient.CreateDatabaseIfNotExistsAsync(db)).Wait()

    let private dbUri = UriFactory.CreateDatabaseUri(dbName)

    let private collUri colId =
        UriFactory.CreateDocumentCollectionUri(dbName, colId.ToString())

    let private docUri colId docId =
        UriFactory.CreateDocumentUri(dbName, colId.ToString(), docId.ToString())

    let private spUri colId spId =
        UriFactory.CreateStoredProcedureUri(dbName, colId.ToString(), spId.ToString())

    let escapeSqlParamPath (paramPath:string) (prefix:string) =
        let pathEls = paramPath.Split('.')
        Array.fold(fun acc (p:string) ->
            acc +
                if ((prefix.Length > 0) || (prefix.Length = 0 && (Array.findIndex((=) p) pathEls) > 0)) then "['" + p + "']"
                else  p) "" pathEls

    let createStoredProcedure colId name content =
        let sp = StoredProcedure()
        sp.Id <- name
        sp.Body <- content
        let res = dbClient.CreateStoredProcedureAsync (collUri <| colId.ToString(), sp)
        res.Result.Resource

    let executeStoredProcedure colId name input =
        let res = dbClient.ExecuteStoredProcedureAsync(spUri colId name, input)
        res.Result.Response

    let deleteStoredProcedure colId name =
        let res = dbClient.DeleteStoredProcedureAsync(spUri colId name)
        res.Result.Resource

    let createCollection dbName throughPut colId storedProcs =
        let coll = DocumentCollection()
        coll.Id <- (colId.ToString())
        let dbUri = UriFactory.CreateDatabaseUri(dbName)
        let options = RequestOptions()
        options.OfferThroughput <- Nullable throughPut
        try
            (dbClient.CreateDocumentCollectionIfNotExistsAsync (dbUri, coll, options)).Wait()
            storedProcs |> List.iter (fun storedProc ->
                let name, javaScript = storedProc
                createStoredProcedure colId name javaScript |> ignore)
            Some ()
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.Conflict)
                    then None else reraise()
                | _ -> reraise()


    let collections () =
        dbClient.CreateDocumentCollectionQuery(dbUri).AsEnumerable()

    let deleteCollection colId =
        try
            (dbClient.DeleteDocumentCollectionAsync(collUri (colId.ToString()) ) ).Wait()
            Some "deleted"
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.NotFound)
                    then None else reraise()
                | _ -> reraise()

    let deleteCollections () =
        collections ()
        |> Seq.iter (fun c -> deleteCollection c.Id |> ignore)

    /// Create a document. The document does not need to have an ID property

    let insert colId record =
        (dbClient.CreateDocumentAsync(collUri (colId.ToString()), record, null, false)).Result

    let tryInsert colId record =
        try
            let resp = insert colId record
            Some resp.Resource.Id
        with
        | :? System.AggregateException as ex ->
            match ex.GetBaseException() with
            | :? DocumentClientException as e when e.StatusCode = Nullable(HttpStatusCode.Conflict) -> None
            | _ -> reraise()

    /// Upsert a document. The document should have an "id" property
    let upsert colId record =
        (dbClient.UpsertDocumentAsync(collUri (colId.ToString()), record, null, false)).Wait()

    /// Replace (AKA update the given document.)
    let replace colId record =
        (dbClient.ReplaceDocumentAsync(collUri (colId.ToString()), record, null)).Wait()

    /// Fetch a document using the id property. Return None if the document does not exist.
    let get<'T> colId docId : 'T option =
        try
            let docUri = docUri colId docId
            Some (dbClient.ReadDocumentAsync<'T>(docUri, null)).Result.Document
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.NotFound)
                    then None else reraise()
                | _ -> reraise()

    /// Remove a document using the id property. Return None if the document does not exist.
    let delete colId docId =
        let docUri = docUri colId docId
        try
            (dbClient.DeleteDocumentAsync(docUri, null)).Result |> ignore
            Some "Deleted"
        with
            | :? System.AggregateException as ex ->
                match ex.GetBaseException() with
                | :? DocumentClientException as dce ->
                    if dce.StatusCode = Nullable<HttpStatusCode>(HttpStatusCode.NotFound)
                    then None else reraise()
                | _ -> reraise()


    let private makeQuery sql  =
        SqlQuerySpec(sql, new SqlParameterCollection())

    /// Execute the given SQL returning a collection of projected documents.
    let query<'T> colId sql =
        let qry = makeQuery sql
        dbClient.CreateDocumentQuery<'T>(collUri colId, qry)
        |> Seq.map id
        |> Seq.toList


    let primCollIds () =
        collections ()
        |> Seq.map (fun dc -> dc.Id :> obj)

    type DocProxy = {
        id : obj
    }

    let deleteDocs colId =
        let sql = "SELECT Document.id, Document._type from Document"
        query<DocProxy> colId sql
        |> List.partition (fun p -> (delete colId p.id).IsSome)

