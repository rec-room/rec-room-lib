#I @".\bin\Debug\net461"
#r "System.dll"
#r "System.Runtime.Serialization"
#r "Microsoft.Azure.Documents.Client.dll"
#r "Newtonsoft.Json.dll"
#load "RecRoom.fs"

open System
open RecRoom.Client

type Address = {
    Street : string
    City : string
    State : string
    PostalCode : string
    Country : string}

type Person = {
  Id: string
  FirstName : string
  LastName : string
  Address : Address
  } with
      member this.id = this.Id
      member _this._type = "Person"
      member _this.partitionKey = "1"


// Get a DB context using the well know local emulator credentials. Create the DB if absent
// let endPoint = Uri("https://localhost:8081")
// let accountKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="

let endPoint = Uri("https://e57666f4-0ee0-4-231-b9ee.documents.azure.com:443/")
let accountKey = "dgomYbZnivntbNcu0uLV6cH2JpLiq5iAOuFeLrMpPXhFWrDAm23Bu4q9uTqAkFRrg3CO5EmlBgwHLuNSILo5hw=="

let dbCntxt = dbContext endPoint accountKey "RRTest" true

// Delete all collections
deleteCollections dbCntxt

// Get a collection context and create the collection if absent.
let collCntxt = collectionContext dbCntxt "TestColl1" 400 true

// Create store, retrive, update and delete a person
let personId = Guid.NewGuid().ToString()
let aPerson = {
  Id = personId
  FirstName = "Jonn"
  LastName = "Bill"
  Address = {
    Street = "123 1st Ave"
    City = "NewSanFran"
    State = "Caledonia"
    PostalCode = "E445BC"
    Country = "GB"}}

insert collCntxt  aPerson
read<Person> collCntxt personId

upsert collCntxt  {aPerson with FirstName = "John"}
read<Person> collCntxt personId

replace collCntxt  {aPerson with FirstName = "Johnny"}
read<Person> collCntxt personId

delete collCntxt personId
tryRead<Person> collCntxt personId


