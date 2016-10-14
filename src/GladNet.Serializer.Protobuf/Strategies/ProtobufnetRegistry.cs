using ProtoBuf.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fasterflect;
using System.Reflection;
using System.Collections;

namespace GladNet.Serializer.Protobuf
{
	/// <summary>
	/// Object provides registry serivces for registering types with
	/// ProtoBuf-net with <see cref="RuntimeTypeModel"/>.
	/// </summary>
	public class ProtobufnetRegistry : ISerializerRegistry
	{
		//TODO: Make this class thread safe.

		//Must be static to surive through multiple instances
		private static Dictionary<Type, object> registeredTypes = new Dictionary<Type, object>();

		public ProtobufnetRegistry()
		{
			if (registeredTypes.ContainsKey(typeof(Stack<int>)))
				return;

			if (RuntimeTypeModel.Default.CanSerialize(typeof(Stack<int>)))
				return;

			//Otherwise we need to register the stack
			ProtoBuf.Meta.RuntimeTypeModel.Default.Add(typeof(Stack<int>), false).SetSurrogate(typeof(StackIntSurrogate));
			
		}

		public bool Register(Type typeToRegister)
		{
			//Ok so here the fun begins.
			//We need a recursive algorithm for walking the graph of the Type to register each
			//serializable type. So, for every member we must look down into that Type recursively and register it.
			//However, we must make sure to check if it's already been registered or we might get circular graphs which
			//would overflow. But that'd be a weird type anyway.

			if (typeToRegister == null)
				throw new ArgumentNullException(nameof(typeToRegister), $"Provided {typeToRegister} is a null arg.");

			if (typeToRegister.IsEnum)
				return true;

			//Can't use isDefined exclusively because it'll fail when doing two-way subtypes
			if (registeredTypes.ContainsKey(typeToRegister))
				return true;

			//Handles collection types
			if (typeof(IEnumerable).IsAssignableFrom(typeToRegister) || typeToRegister.IsArray)
				if (typeToRegister.IsArray)
					Register(typeToRegister.GetElementType());
				else
					foreach (var gparam in typeToRegister.GetGenericArguments())
						Register(gparam);

			//If it's not defined we need to add it.
			if (typeToRegister.Attribute<GladNetSerializationContractAttribute>() == null)
				return false;

			MetaType typeModel = RuntimeTypeModel.Default.Add(typeToRegister, false);

			//Add each member
			foreach (MemberInfo mi in typeToRegister.MembersWith<GladNetMemberAttribute>(MemberTypes.Field | MemberTypes.Property, Flags.InstanceAnyDeclaredOnly)) //keep this declare only because of Unity3D serialization issues with NetSendable
			{
				int tagID = mi.Attribute<GladNetMemberAttribute>().TagID;

				if (typeModel[tagID] == null)
					typeModel.Add(mi.Attribute<GladNetMemberAttribute>().TagID, mi.Name);

				//Now we might need to register this type aswell.
				//Recur to try to register this type
				Register(mi.Type());
			}

			//If might have a include on it so we should check it to register it with the subtype

			IEnumerable<GladNetSerializationIncludeAttribute> includes = typeToRegister.Attributes<GladNetSerializationIncludeAttribute>();

			foreach (GladNetSerializationIncludeAttribute include in includes)
			{
				//Before doing anything we should register the include type first
				this.Register(include.TypeToWireTo);

				//this is the simple case; however unlike protobuf we support two-include
				if (include.IncludeForDerived)
				{
					typeModel.AddSubType(include.TagID, include.TypeToWireTo);
				}
				else
				{
					//this is not for mappping a base type to setup mapping for its child
					//we need to map this child to its base
					//so we need to get the MetaType for it

					//If the include is not yet known about (check the index)
					if(RuntimeTypeModel.Default[include.TypeToWireTo][include.TagID] == null)
						RuntimeTypeModel.Default[include.TypeToWireTo]
							.AddSubType(include.TagID, typeToRegister);
				}
			}

			registeredTypes.Add(typeToRegister, null);

			return true;
		}
	}
}