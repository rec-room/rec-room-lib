namespace RecRoom

module SerializationUtil =

  open System
  open Microsoft.FSharp.Reflection
  open System.Net
  open System.Net.Mail

  open Newtonsoft.Json
  open Newtonsoft.Json.Converters
  open ReflectionUtil


  exception DeserializationException of typ : Type * value : string * path : string
  exception RequiredPropertiesDeserializationException of typ : Type * properties : string [] * path : string


  let fromString(str:String) (t:Type) (path:string) =
      let x = t.GetMethod("FromString").Invoke(null, [|str |])
      match x with
      | null -> raise <| DeserializationException(t, str, path)
      | x ->
      let x' = x.GetType().GetProperty("Value")
      match x' with
      | null -> raise <| DeserializationException(t, str, path)
      | prop -> prop.GetValue(x,null)


  ///Contract resolver suppressing mutable serialization and etag field serialization
  type CustomContractResolver () =
    inherit Serialization.DefaultContractResolver()

    override __.CreateProperty(mmbr, serialization) =
      let prop = base.CreateProperty(mmbr, serialization)
      if prop.PropertyName.EndsWith "@" then
        prop.ShouldSerialize <- (fun _ -> false)
      if prop.PropertyName = "_etag" then
        prop.ShouldSerialize <- (fun _ -> false)
      prop


  type IPAddressConverter() =
    inherit JsonConverter()

    override _x.CanConvert(t:System.Type) =
      t = typeof<IPAddress>

    override _x.WriteJson(writer, value, serializer) =
      serializer.Serialize(writer, value.ToString())

    override _x.ReadJson(reader, t, _existingValue, serializer) =
      let path = reader.Path
      let value = serializer.Deserialize<string> reader
      match IPAddress.TryParse(value) with
      | (true, ip) -> unbox ip
      | (false, _) -> raise <| DeserializationException(t, value,path)

    override _x.CanRead = true
    override _x.CanWrite = true

  type MailAddressConverter() =
    inherit JsonConverter()

    override _x.CanConvert(t:System.Type) =
      t = typeof<MailAddress>

    override _x.WriteJson(writer, value, serializer) =
      serializer.Serialize(writer, value.ToString())

    override _x.ReadJson(reader, t, _existingValue, serializer) =
      let path = reader.Path
      let value = serializer.Deserialize<string> reader
      try
        unbox (MailAddress(value))
      with
      | _ -> raise <| DeserializationException(t, value,path)

    override _x.CanRead = true
    override _x.CanWrite = true

  type OptionConverter() =
    inherit JsonConverter()

    override _x.CanConvert(t) =
      t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    override _x.WriteJson(writer, value, serializer) =
      if isNull value then ()
      else
        let _,fields = FSharpValue.GetUnionFields(value, value.GetType())
        let value = fields.[0]
        serializer.Serialize(writer, value)

    override _x.ReadJson(reader, t, _existingValue, serializer) =
      let innerType = t.GetGenericArguments().[0]
      let innerType =
        if innerType.IsValueType then (typedefof<Nullable<_>>).MakeGenericType([|innerType|])
        else innerType
      let value = serializer.Deserialize(reader, innerType)
      let cases = FSharpType.GetUnionCases(t)
      if isNull value then FSharpValue.MakeUnion(cases.[0], [||])
      else FSharpValue.MakeUnion(cases.[1], [|value|])

    override _x.CanRead = true
    override _x.CanWrite = true


  // This converter is needed because Json.Net 9.0.1 does not support serialization
  // of FSharp 4.0.1 lists. Once we can move to a higher version of Json.Net we no
  // longer need this.
  type ListConverter() =
    inherit JsonConverter()

    override _x.CanConvert(t) =
      t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

    override _x.WriteJson(_, _, _) =
      failwith "Should never be called"

    override _x.ReadJson(reader, t, _existingValue, serializer) =

      // Currently only supports obj lists. If need other prim type such as int32
      // we need to extend this. For example by adding more functions like this.
      let makeListOf itemType (items : obj list)  =
          let listType = (typedefof<Microsoft.FSharp.Collections.List<_>>).MakeGenericType ([| itemType |])
          let add =
              let cons = listType.GetMethod ("Cons")
              fun item list -> cons.Invoke (null, [| item; list; |])
          let list =
              let empty = listType.GetProperty ("Empty")
              empty.GetValue (null)
          list |> List.foldBack add items


      let itemType = t.GetGenericArguments().[0]
      let arrayType = itemType.MakeArrayType(1)
      let value = serializer.Deserialize(reader, arrayType)
      let a = value :?> obj array |> List.ofArray
      makeListOf itemType a

    override _x.CanRead = true
    override _x.CanWrite = false


  type NoDataDUConverter() =
    inherit JsonConverter()

    override _x.CanConvert(t:System.Type) = true

    override _x.WriteJson(writer,value, serializer) =
      serializer.Serialize(writer, value.ToString())

    override _x.ReadJson(reader, t, _existingValue, serializer) =
      let str = serializer.Deserialize<string> reader
      let path = reader.Path
      match tryGetCase t str with
      | None -> raise <| DeserializationException(t, str, path)
      | Some x -> x


  let dateTimeConverter = IsoDateTimeConverter()
  dateTimeConverter.DateTimeFormat <- "yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffff'Z'"

  let miscConverters = [|
      dateTimeConverter :> JsonConverter
      IPAddressConverter() :> JsonConverter
      MailAddressConverter() :> JsonConverter
      OptionConverter() :> JsonConverter
      ListConverter() :> JsonConverter
  |]

  let serializerSettings =
      let settings = JsonSerializerSettings()
      settings.Converters <- miscConverters
      settings.NullValueHandling <- NullValueHandling.Ignore
      settings.MissingMemberHandling <- MissingMemberHandling.Ignore
      settings.ContractResolver <- CustomContractResolver()
      settings


  // Couple of utility methods for vanilla serialization.
  let serialize a =
    JsonConvert.SerializeObject(a, Formatting.Indented, serializerSettings)

  let deserialize<'a> input =
    try
      JsonConvert.DeserializeObject<'a>(input, serializerSettings)
    with
      | :? (DeserializationException) as ex ->
      printfn "%s" (ex.Message + " " + ex.path + ex.typ.ToString() + " " + ex.value) ;
      reraise()


