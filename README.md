# Rec-Room
A place for your FSharp Records

A library that supports storing, fetching, updateing, deleting and querying of FSharp records to a persistant store. The storage engine is Azure CosmosDB. Design goals are:
* Simple interface for storing FSharp records. It is not intended to be a general API layer for CosmosDB
* Idiomatic for for FSharp developers.
* Typesafe as possible

## Example Usage

```
module RecRoomDemo
open Newtonsoft.Json
open RecRoom


  
[<JsonConverter(typeof<Serialization.DUConverter>)>]
type ColId = MakeColId of string


[<JsonConverter(typeof<Serialization.DUConverter>)>]
type Occupation =
| BusDriver
| Doctor
| Cook
| Pilot

type Address = {
    Street : string
    City = string
    State = string
    PostalCode = string
    Country = string}

[<JsonConverter(typeof<Serialization.DUConverter>)>]
type PersonId = PersonId of string
type Person = {
  // All CosmosDB documents MUST have a property called id (lowercase)
  id: PersonId
  FirstName string
  LastName = string
  Address : Address
  Occupation = Doctor
  } with
    member _this._type = "Person"

[<JsonConverter(typeof<Serialization.DUConverter>)>]
type CarId = CarId of string
type Car = {
  // All CosmosDB documents MUST have a property called 'id' (lowercase)
  id : CarId
  Manufacturer : string
  Model: string
  Serial: Address
  Owner: PersonId
  } with
    // so documents can be found and mapped to correct FSharp type
    member _this._type = "Car"

// Get a DB client and create a collection.
let client = newClient endPoint accountKey serializerSettings connectionPolicy
let collId = CollId "Collection"
let throughPut = 400
let storedProcedures = []
createCollection throughPut collId storedProcedures


let personId = PersonId "1234"
let aPerson = {
  id = personId
  FirstName = "Jonn"
  LastName = "Bill"
  Address = {
    Street = "123 1st Ave
    City = "NewSanFran"
    State = "Caledonia"
    PostalCode = "E445BC"
    Country = "GB"}
  Occupation = Doctor}
  
let carId = CarId "23452"
let car = {
  id = carId
  Manufacturer = "Ford"
  Model = "Mustang"
  Serial = "Mus-876543"
  Owner = personId}
  
  
// Operations that should logically should succeed do not return options but will raise exceptions
// in case if intrastructure failure (network, db etc, duplicate id).
insert client collId aPerson
insert client collId aCar
upsert client collId {aPerson with FirstName = "John"} 

// Operations that could logically fail come with try... versions. For example in the following two
// cases there may not be an item with the given id.
update client collId {aPerson with FirstName = "John"} //does not default to upsert
tryUpdate client collId {aPerson with FirstName = "John"}
let person = get<Pserson> client collId docId // fails if collId does not exist
let person = tryGet<Person

let cars = query<Car> client collId "WHERE Car.PersonId = " + carId.ToString()

tryDelete client collId (PersonId "foo")
delete client collId personId
```

