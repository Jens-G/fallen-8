// 
// VertexModelDefinition.cs
//  
// Author:
//       Henning Rauch <Henning@RauchEntwicklung.biz>
// 
// Copyright (c) 2011 Henning Rauch
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;

namespace Fallen8.API.Helper
{
	/// <summary>
	/// Vertex model definition.
	/// </summary>
	public sealed class VertexModelDefinition
	{
		/// <summary>
		/// Adds a property.
		/// </summary>
		/// <returns>
		/// The vertex model definition.
		/// </returns>
		/// <param name='id'>
		/// Identifier.
		/// </param>
		/// <param name='property'>
		/// Property.
		/// </param>
		public VertexModelDefinition AddProperty (Int64 id, IComparable property)
		{
			throw new NotImplementedException ();
		}
		
		/// <summary>
		/// Adds a string property.
		/// </summary>
		/// <returns>
		/// The vertex model definition.
		/// </returns>
		/// <param name='name'>
		/// Name.
		/// </param>
		/// <param name='property'>
		/// Property.
		/// </param>
		public VertexModelDefinition AddStringProperty (String name, IComparable property)
		{
			throw new NotImplementedException ();
		}
		
		/// <summary>
		/// Adds an edge.
		/// </summary>
		/// <returns>
		/// The vertex model definition.
		/// </returns>
		/// <param name='id'>
		/// Identifier.
		/// </param>
		/// <param name='edgeDefinition'>
		/// Edge definition.
        /// </param>
		public VertexModelDefinition AddEdge (Int64 id, EdgeModelDefinition edgeDefinition)
		{
			throw new NotImplementedException ();
		}

        /// <summary>
        /// Sets the creation date.
        /// </summary>
        /// <returns>
        /// The vertex model definition.
        /// </returns>
        /// <param name='name'>
        /// Creation date.
        /// </param>
        public VertexModelDefinition SetCreationDate(DateTime creationDate)
        {
            throw new NotImplementedException();
        }
	}
}