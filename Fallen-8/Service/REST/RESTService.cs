// 
//  RESTService.cs
//  
//  Author:
//       Henning Rauch <Henning@RauchEntwicklung.biz>
//  
//  Copyright (c) 2012 Henning Rauch
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

#region Usings

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using Jint;
using NoSQL.GraphDB.Algorithms.Path;
using NoSQL.GraphDB.Helper;
using NoSQL.GraphDB.Index;
using NoSQL.GraphDB.Index.Fulltext;
using NoSQL.GraphDB.Index.Spatial;
using NoSQL.GraphDB.Log;
using NoSQL.GraphDB.Model;
using NoSQL.GraphDB.Plugin;
using NoSQL.GraphDB.Service.REST.Ressource;
using NoSQL.GraphDB.Service.REST.Result;
using NoSQL.GraphDB.Service.REST.Specification;

#endregion

namespace NoSQL.GraphDB.Service.REST
{
    /// <summary>
    ///   Fallen-8 REST service.
    /// </summary>
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, IncludeExceptionDetailInFaults = true)]
    public sealed class RESTService : IRESTService, IDisposable
    {
        #region Data

        /// <summary>
        ///   The internal Fallen-8 instance
        /// </summary>
        private readonly NoSQL.GraphDB.Fallen8 _fallen8;

        /// <summary>
        ///   The ressources.
        /// </summary>
        private Dictionary<String, MemoryStream> _ressources;

        /// <summary>
        ///   The html befor the code injection
        /// </summary>
        private String _frontEndPre;

        /// <summary>
        ///   The html after the code injection
        /// </summary>
        private String _frontEndPost;

        /// <summary>
        /// The Fallen-8 save path
        /// </summary>
        private String _savePath;

        /// <summary>
        /// The Fallen-8 save file
        /// </summary>
        private String _saveFile;

        /// <summary>
        /// The optimal number of partitions
        /// </summary>
        private UInt32 _optimalNumberOfPartitions;

        #endregion

        #region Constructor

        /// <summary>
        ///   Initializes a new instance of the RESTService class.
        /// </summary>
        /// <param name='fallen8'> Fallen-8. </param>
        public RESTService(NoSQL.GraphDB.Fallen8 fallen8)
        {
            _fallen8 = fallen8;
            LoadFrontend();

            _saveFile = "Temp.f8s";
            _savePath = Environment.CurrentDirectory + System.IO.Path.DirectorySeparatorChar + _saveFile;

            _optimalNumberOfPartitions = Convert.ToUInt32(Environment.ProcessorCount * 3 / 2);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            //do nothing atm
        }

        #endregion

        #region IRESTService implementation

        public int AddVertex(VertexSpecification definition)
        {
            #region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

            return _fallen8.CreateVertex(definition.CreationDate, GenerateProperties(definition.Properties)).Id;
        }

        public int AddEdge(EdgeSpecification definition)
        {
            #region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

            return
                _fallen8.CreateEdge(definition.SourceVertex, definition.EdgePropertyId, definition.TargetVertex,
                                    definition.CreationDate, GenerateProperties(definition.Properties)).Id;
        }

        public PropertiesREST GetAllVertexProperties(string vertexIdentifier)
        {
            return GetGraphElementProperties(vertexIdentifier);
        }

        public PropertiesREST GetAllEdgeProperties(string edgeIdentifier)
        {
            return GetGraphElementProperties(edgeIdentifier);
        }

        public List<ushort> GetAllAvailableOutEdgesOnVertex(string vertexIdentifier)
        {
            VertexModel vertex;
            return _fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier))
                       ? vertex.GetOutgoingEdgeIds()
                       : null;
        }

        public List<ushort> GetAllAvailableIncEdgesOnVertex(string vertexIdentifier)
        {
            VertexModel vertex;
            return _fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier))
                       ? vertex.GetIncomingEdgeIds()
                       : null;
        }

        public List<int> GetOutgoingEdges(string vertexIdentifier, string edgePropertyIdentifier)
        {
            VertexModel vertex;
            if (_fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
                ReadOnlyCollection<EdgeModel> edges;
                if (vertex.TryGetOutEdge(out edges, Convert.ToInt32(edgePropertyIdentifier)))
                {
                    return edges.Select(_ => _.Id).ToList();
                }
            }
            return null;
        }

        public List<int> GetIncomingEdges(string vertexIdentifier, string edgePropertyIdentifier)
        {
            VertexModel vertex;
            if (_fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
                ReadOnlyCollection<EdgeModel> edges;
                if (vertex.TryGetInEdge(out edges, Convert.ToInt32(edgePropertyIdentifier)))
                {
                    return edges.Select(_ => _.Id).ToList();
                }
            }
            return null;
        }

        public void Trim()
        {
            _fallen8.Trim();
        }

        public StatusREST Status()
        {
			var freeBytesOfMemory = GetFreeMemory();
			var totalBytesOfMemoryUsed = GetTotalMemory() - freeBytesOfMemory;

            var vertexCount = _fallen8.VertexCount;
            var edgeCount = _fallen8.EdgeCount;

            IEnumerable<String> availableIndices;
            PluginFactory.TryGetAvailablePlugins<IIndex>(out availableIndices);

            IEnumerable<String> availablePathAlgos;
            PluginFactory.TryGetAvailablePlugins<IShortestPathAlgorithm>(out availablePathAlgos);

            IEnumerable<String> availableServices;
            PluginFactory.TryGetAvailablePlugins<IService>(out availableServices);

            return new StatusREST
                       {
                           AvailableIndexPlugins = new List<String>(availableIndices),
                           AvailablePathPlugins = new List<String>(availablePathAlgos),
                           AvailableServicePlugins = new List<String>(availableServices),
                           EdgeCount = edgeCount,
                           VertexCount = vertexCount,
                           UsedMemory = totalBytesOfMemoryUsed,
                           FreeMemory = freeBytesOfMemory
                       };
        }

        public Stream GetFrontend()
        {
            if (WebOperationContext.Current != null)
            {
                var baseUri = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.BaseUri;

                WebOperationContext.Current.OutgoingResponse.ContentType = "text/html";

                var sb = new StringBuilder();

                sb.Append(_frontEndPre);
                sb.Append(Environment.NewLine);
                sb.AppendLine("var baseUri = \"" + baseUri + "\";" + Environment.NewLine);
                sb.Append(_frontEndPost);

                return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
            }

            return new MemoryStream(Encoding.UTF8.GetBytes("Sorry, no frontend available."));
        }

        public void ReloadFrontend()
        {
            LoadFrontend();
        }

        public Stream GetFrontendRessources(String ressourceName)
        {
            MemoryStream ressourceStream;
            if (_ressources.TryGetValue(ressourceName, out ressourceStream))
            {
                var result = new MemoryStream();
                var buffer = new byte[32768];
                int read;
                while ((read = ressourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    result.Write(buffer, 0, read);
                }
                ressourceStream.Position = 0;
                result.Position = 0;

                if (WebOperationContext.Current != null)
                {
                    var extension = ressourceName.Split('.').Last();

                    switch (extension)
                    {
                        case "html":
                        case "htm":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "text/html";
                            break;
                        case "png":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "image/png";
                            break;
                        case "css":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "text/css";
                            break;
                        case "gif":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "image/gif";
                            break;
                        case "ico":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "image/ico";
                            break;
                        case "swf":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "application/x-shockwave-flash";
                            break;
                        case "js":
                            WebOperationContext.Current.OutgoingResponse.ContentType = "text/javascript";
                            break;
                        default:
                            throw new ApplicationException(String.Format("File type {0} not supported", extension));
                    }
                }

                return result;
            }

            return null;
        }

        public IEnumerable<int> GraphScan(String propertyIdString, ScanSpecification definition)
        {
            #region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

            var propertyId = Convert.ToUInt16(propertyIdString);

            var value = (IComparable) Convert.ChangeType(definition.Literal.Value,
                                                         Type.GetType(definition.Literal.FullQualifiedTypeName, true,
                                                                      true));

            List<AGraphElement> graphElements;
            return _fallen8.GraphScan(out graphElements, propertyId, value, definition.Operator)
                       ? CreateResult(graphElements, definition.ResultType)
                       : Enumerable.Empty<Int32>();
        }

        public IEnumerable<int> IndexScan(string indexId, ScanSpecification definition)
        {
            #region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

            var value = (IComparable) Convert.ChangeType(definition.Literal.Value,
                                                         Type.GetType(definition.Literal.FullQualifiedTypeName, true,
                                                                      true));

            ReadOnlyCollection<AGraphElement> graphElements;
            return _fallen8.IndexScan(out graphElements, indexId, value, definition.Operator)
                       ? CreateResult(graphElements, definition.ResultType)
                       : Enumerable.Empty<Int32>();
        }

        public IEnumerable<int> RangeIndexScan(string indexId, RangeScanSpecification definition)
        {
            #region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

            var left = (IComparable) Convert.ChangeType(definition.LeftLimit,
                                                        Type.GetType(definition.FullQualifiedTypeName, true, true));

            var right = (IComparable) Convert.ChangeType(definition.RightLimit,
                                                         Type.GetType(definition.FullQualifiedTypeName, true, true));

            ReadOnlyCollection<AGraphElement> graphElements;
            return _fallen8.RangeIndexScan(out graphElements, indexId, left, right, definition.IncludeLeft,
                                           definition.IncludeRight)
                       ? CreateResult(graphElements, definition.ResultType)
                       : Enumerable.Empty<Int32>();
        }

		public FulltextSearchResultREST FulltextIndexScan (string indexId, FulltextScanSpecification definition)
		{
			#region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

			FulltextSearchResult result;
            return _fallen8.FulltextIndexScan(out result, indexId, definition.RequestString)
                       ? new FulltextSearchResultREST(result)
                       : null;
		}

		public IEnumerable<int> SpatialIndexScanSearchDistance (string indexId, SearchDistanceSpecification definition)
		{
			#region initial checks

            if (definition == null)
            {
                throw new ArgumentNullException("definition");
            }

            #endregion

			AGraphElement graphElement;
			if (_fallen8.TryGetGraphElement(out graphElement, definition.GraphElementId)) 
			{
				IIndex idx;
				if (_fallen8.IndexFactory.TryGetIndex(out idx, indexId)) 
				{
					var spatialIndex = idx as ISpatialIndex;
					if (spatialIndex != null) 
					{
						ReadOnlyCollection<AGraphElement> result;
						return spatialIndex.SearchDistance(out result, definition.Distance, graphElement)
							? result.Select(_ => _.Id)
							: null;
					}
					Logger.LogError(string.Format("The index with id {0} is no spatial index.", indexId));
					return null;
				}
				Logger.LogError(string.Format("Could not find index {0}.", indexId));
				return null;
			}
			Logger.LogError(string.Format("Could not find graph element {0}.", definition.GraphElementId));
			return null;
		}

        public void Load(string startServices)
        {
            _fallen8.Load(FindLatestFallen8(), Convert.ToBoolean(startServices));
        }

        public void Save()
        {
            _fallen8.Save(_savePath, _optimalNumberOfPartitions);
        }

		public bool TryAddProperty (string graphElementIdString, string propertyIdString, PropertySpecification definition)
		{
			var graphElementId = Convert.ToInt32(graphElementIdString);
			var propertyId = Convert.ToUInt16(propertyIdString);

			var property = Convert.ChangeType(
				definition.Property, 
				Type.GetType(definition.FullQualifiedTypeName, true, true));

			return _fallen8.TryAddProperty(graphElementId, propertyId, property);
		}

		public bool TryRemoveProperty (string graphElementIdString, string propertyIdString)
		{
			var graphElementId = Convert.ToInt32(graphElementIdString);
			var propertyId = Convert.ToUInt16(propertyIdString);

			return _fallen8.TryRemoveProperty(graphElementId, propertyId);
		}

		public bool TryRemoveGraphElement (string graphElementIdString)
		{
			var graphElementId = Convert.ToInt32(graphElementIdString);

			return _fallen8.TryRemoveGraphElement(graphElementId);
		}

		public void TabulaRasa ()
		{
			_fallen8.TabulaRasa();
		}

		public uint VertexCount ()
		{
			return _fallen8.VertexCount;
		}

		public uint EdgeCount ()
		{
			return _fallen8.EdgeCount;
		}

		public UInt64 FreeMem ()
		{
			return GetFreeMemory();
		}

		public uint GetInDegree (string vertexIdentifier)
		{
			VertexModel vertex;
            if (_fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
				return vertex.GetInDegree();
            }
            return 0;
		}

		public uint GetOutDegree (string vertexIdentifier)
		{
			VertexModel vertex;
            if (_fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
				return vertex.GetOutDegree();
            }
            return 0;
		}

		public uint GetInEdgeDegree (string vertexIdentifier, string edgePropertyIdentifier)
		{
			VertexModel vertex;
            if (_fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
                ReadOnlyCollection<EdgeModel> edges;
                if (vertex.TryGetInEdge(out edges, Convert.ToInt32(edgePropertyIdentifier)))
                {
                    return Convert.ToUInt32(edges.Count);
                }
            }
            return 0;
		}

		public uint GetOutEdgeDegree (string vertexIdentifier, string edgePropertyIdentifier)
		{
			VertexModel vertex;
            if (_fallen8.TryGetVertex(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
                ReadOnlyCollection<EdgeModel> edges;
                if (vertex.TryGetOutEdge(out edges, Convert.ToInt32(edgePropertyIdentifier)))
                {
                    return Convert.ToUInt32(edges.Count);
                }
            }
            return 0;
		}

        public List<PathREST> GetPaths(string from, string to, PathSpecification definition)
        {
            if (definition != null)
            {
                var fromId = Convert.ToInt32(from);
                var toId = Convert.ToInt32(to);

                var engine = new JintEngine();

                var edgeCostDelegate = CreateEdgeCostDelegate(definition.Cost, engine);
                var vertexCostDelegate = CreateVertexCostDelegate(definition.Cost, engine);

                var edgePropertyFilterDelegate = CreateEdgePropertyFilterDelegate(definition.Filter, engine);
                var vertexFilterDelegate = CreateVertexFilterDelegate(definition.Filter, engine);
                var edgeFilterDelegate = CreateEdgeFilterDelegate(definition.Filter, engine);

                List<NoSQL.GraphDB.Algorithms.Path.Path> paths;
                if (_fallen8.CalculateShortestPath(
                    out paths,
                    definition.PathAlgorithmName,
                    fromId,
                    toId,
                    definition.MaxDepth,
                    definition.MaxPathWeight,
                    definition.MaxResults,
                    edgePropertyFilterDelegate,
                    vertexFilterDelegate,
                    edgeFilterDelegate,
                    edgeCostDelegate,
                    vertexCostDelegate))
                {
                    if (paths != null)
                    {
                        return new List<PathREST>(paths.Select(aPath => new PathREST(aPath)));
                    }
                }
            }
            return null;
        }

		public List<PathREST> GetPathsByVertex (string from, string to, PathSpecification definition)
		{
			return GetPaths(from, to, definition);
		}


		public bool CreateIndex (PluginSpecification definition)
		{
			IIndex result;
			return _fallen8.IndexFactory.TryCreateIndex(out result, definition.UniqueId, definition.PluginType, CreatePluginOptions(definition.PluginOptions));
		}

		public bool AddToIndex (IndexAddToSpecification definition)
		{
			IIndex idx;
			if (_fallen8.IndexFactory.TryGetIndex(out idx, definition.IndexId)) 
			{
				AGraphElement graphElement;
				if (_fallen8.TryGetGraphElement(out graphElement, definition.GraphElementId)) 
				{
					idx.AddOrUpdate(CreateObject(definition.Key), graphElement);
					return true; 
				}

				Logger.LogError(String.Format("Could not find graph element {0}.", definition.GraphElementId));
				return false;
			}
			Logger.LogError(String.Format("Could not find index {0}.", definition.IndexId));
			return false;
		}

		public bool RemoveKeyFromIndex (IndexRemoveKeyFromIndexSpecification definition)
		{
			IIndex idx;
			if (_fallen8.IndexFactory.TryGetIndex(out idx, definition.IndexId)) 
			{
				return idx.TryRemoveKey(CreateObject(definition.Key));
			}
			Logger.LogError(String.Format("Could not find index {0}.", definition.IndexId));
			return false;
		}

		public bool RemoveGraphElementFromIndex (IndexRemoveGraphelementFromIndexSpecification definition)
		{
			IIndex idx;
			if (_fallen8.IndexFactory.TryGetIndex(out idx, definition.IndexId)) 
			{
				AGraphElement graphElement;
				if (_fallen8.TryGetGraphElement(out graphElement, definition.GraphElementId)) 
				{
					idx.RemoveValue(graphElement);
					return true; 
				}

				Logger.LogError(String.Format("Could not find graph element {0}.", definition.GraphElementId));
				return false;
			}
			Logger.LogError(String.Format("Could not find index {0}.", definition.IndexId));
			return false;
		}

		public bool DeleteIndex (IndexDeleteSpecificaton definition)
		{
			return _fallen8.IndexFactory.TryDeleteIndex(definition.IndexId);
		}

		public bool CreateService (PluginSpecification definition)
		{
			IService service;
			return _fallen8.ServiceFactory.TryAddService(out service, definition.PluginType, definition.UniqueId, CreatePluginOptions(definition.PluginOptions));
		}

		public bool DeleteService (ServiceDeleteSpecificaton definition)
		{
			return _fallen8.ServiceFactory.Services.Remove(definition.ServiceId);
		}

		#endregion

        #region private helper

		/// <summary>
		/// Creates the plugin options.
		/// </summary>
		/// <returns>
		/// The plugin options.
		/// </returns>
		/// <param name='options'>
		/// Options.
		/// </param>
		IDictionary<string, object> CreatePluginOptions (Dictionary<string, PropertySpecification> options)
		{
			return options.ToDictionary(key => key.Key, value => CreateObject(value.Value));
		}

		/// <summary>
		/// Creates the object.
		/// </summary>
		/// <returns>
		/// The object.
		/// </returns>
		/// <param name='key'>
		/// Key.
		/// </param>
		private static object CreateObject (PropertySpecification key)
		{
			return Convert.ChangeType(
				key.Property,
				Type.GetType(key.FullQualifiedTypeName,true, true));
		}

        /// <summary>
        /// Creates an edge filter delegate
        /// </summary>
        /// <param name="pathFilterSpecification">Filter specification.</param>
        /// <param name="engine">Jint engine</param>
        /// <returns>The delegate</returns>
        private PathDelegates.EdgeFilter CreateEdgeFilterDelegate(PathFilterSpecification pathFilterSpecification, JintEngine engine)
        {
            if (pathFilterSpecification != null && !String.IsNullOrEmpty(pathFilterSpecification.Edge))
            {
                engine.Run(pathFilterSpecification.Edge);

                return delegate(EdgeModel edge, Direction direction)
                           {
                               return Convert.ToBoolean(engine.CallFunction(Constants.EdgeFilterFuncName, 
                                   edge.Id,
                                   direction.ToString(),
                                   edge.CreationDate,
                                   edge.ModificationDate,
                                   edge.GetAllProperties().ToDictionary(
                                        key => key.PropertyId,
                                        value => value.Value)));
                           };
            }
            return null;
        }

        /// <summary>
        /// Creates a vertex filter delegate
        /// </summary>
        /// <param name="pathFilterSpecification">Filter specification.</param>
        /// <param name="engine">Jint engine</param>
        /// <returns>The delegate</returns>
        private PathDelegates.VertexFilter CreateVertexFilterDelegate(PathFilterSpecification pathFilterSpecification, JintEngine engine)
        {
            if (pathFilterSpecification != null && !String.IsNullOrEmpty(pathFilterSpecification.Vertex))
            {
                engine.Run(pathFilterSpecification.Vertex);

                return delegate(VertexModel vertex)
                {
                    return Convert.ToBoolean(engine.CallFunction(Constants.VertexFilterFuncName,
                        vertex.Id,
                        vertex.CreationDate,
                        vertex.ModificationDate,
                        vertex.GetInDegree(),
                        vertex.GetOutDegree(),
                        vertex.GetAllProperties().ToDictionary(
                             key => key.PropertyId,
                             value => value.Value)));
                };
            }
            return null;
        }

        /// <summary>
        /// Creates an edge property filter delegate
        /// </summary>
        /// <param name="pathFilterSpecification">Filter specification.</param>
        /// <param name="engine">Jint engine</param>
        /// <returns>The delegate</returns>
        private PathDelegates.EdgePropertyFilter CreateEdgePropertyFilterDelegate(PathFilterSpecification pathFilterSpecification, JintEngine engine)
        {
            if (pathFilterSpecification != null && !String.IsNullOrEmpty(pathFilterSpecification.EdgeProperty))
            {
                engine.Run(pathFilterSpecification.EdgeProperty);

                return delegate(ushort id, Direction direction)
                           {
                               return Convert.ToBoolean(engine.CallFunction(Constants.EdgePropertyFilterFuncName,
                                                                            id,
                                                                            direction.ToString()));
                           };
            }
            return null;
        }

        /// <summary>
        /// Creates a vertex cost delegate
        /// </summary>
        /// <param name="pathCostSpecification">Cost specificateion</param>
        /// <param name="engine">Jint engine</param>
        /// <returns>The delegate</returns>
        private PathDelegates.VertexCost CreateVertexCostDelegate(PathCostSpecification pathCostSpecification, JintEngine engine)
        {
            if (pathCostSpecification != null && !String.IsNullOrEmpty(pathCostSpecification.Vertex))
            {
                engine.Run(pathCostSpecification.Vertex);

                return delegate(VertexModel vertex)
                           {
                               return Convert.ToDouble(engine.CallFunction(Constants.VertexCostFuncName,
                                                                           vertex.Id,
                                                                           vertex.CreationDate,
                                                                           vertex.ModificationDate,
                                                                           vertex.GetInDegree(),
                                                                           vertex.GetOutDegree(),
                                                                           vertex.GetAllProperties().ToDictionary(
                                                                               key => key.PropertyId,
                                                                               value => value.Value)));
                           };
            }
            return null;
        }

        /// <summary>
        /// Creates an edge cost delegate
        /// </summary>
        /// <param name="pathCostSpecification">Cost specificateion</param>
        /// <param name="engine">Jint engine</param>
        /// <returns>The delegate</returns>
        private PathDelegates.EdgeCost CreateEdgeCostDelegate(PathCostSpecification pathCostSpecification, JintEngine engine)
        {
            if (pathCostSpecification != null && !String.IsNullOrEmpty(pathCostSpecification.Edge))
            {
                engine.Run(pathCostSpecification.Edge);

                return delegate(EdgeModel edge)
                {
                    return Convert.ToDouble(engine.CallFunction(Constants.EdgeCostFuncName,
                                                                edge.Id,
                                                                edge.CreationDate,
                                                                edge.ModificationDate,
                                                                edge.GetAllProperties().ToDictionary(
                                                                    key => key.PropertyId,
                                                                    value => value.Value)));
                };
            }
            return null;
        }

        /// <summary>
        /// Get the free memory of the system
        /// </summary>
        /// <returns>Free memory in bytes</returns>
		private UInt64 GetFreeMemory ()
		{
			#if __MonoCS__
    			//mono specific code
				var pc = new PerformanceCounter("Mono Memory", "Total Physical Memory");
            	var totalMemory = (ulong)pc.RawValue;

				Process.GetCurrentProcess().Refresh();
            	var usedMemory = (ulong)Process.GetCurrentProcess().WorkingSet64;

				return totalMemory - usedMemory;
			#else
                var freeMem = new PerformanceCounter("Memory", "Available Bytes");
            	return Convert.ToUInt64(freeMem.NextValue());
			#endif
		}

        /// <summary>
        /// Gets the total memory of the system
        /// </summary>
        /// <returns>Total memory in bytes</returns>
		private UInt64 GetTotalMemory ()
		{
			#if __MonoCS__
    			//mono specific code
				var pc = new PerformanceCounter("Mono Memory", "Total Physical Memory");
            	return (ulong)pc.RawValue;
			#else
            var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();	
				return computerInfo.TotalPhysicalMemory;
			#endif
		}

        /// <summary>
        /// Searches for the latest fallen-8
        /// </summary>
        /// <returns></returns>
        private string FindLatestFallen8()
        {
            var versions = System.IO.Directory.EnumerateFiles(Environment.CurrentDirectory,
                                               _saveFile + Constants.VersionSeparator + "*")
                                               .ToList();

            if (versions.Count > 0)
            {
                var fileToPathMapper = versions
                    .Select(path => path.Split(System.IO.Path.DirectorySeparatorChar))
                    .Where(_ => !_.Last().Contains(Constants.GraphElementsSaveString))
                    .Where(_ => !_.Last().Contains(Constants.IndexSaveString))
                    .Where(_ => !_.Last().Contains(Constants.ServiceSaveString))
                    .ToDictionary(key => key.Last(), value => value.Aggregate((a, b) => a + System.IO.Path.DirectorySeparatorChar + b));

                var latestRevision = fileToPathMapper
                    .Select(file => file.Key.Split(Constants.VersionSeparator)[1])
                    .Select(revisionString => DateTime.FromBinary(Convert.ToInt64(revisionString)))
                    .OrderByDescending(revision => revision)
                    .First()
                    .ToBinary()
                    .ToString(CultureInfo.InvariantCulture);

                return fileToPathMapper.First(_ => _.Key.Contains(latestRevision)).Value;
            }
            return _savePath;
        }

        /// <summary>
        ///   Creats the result
        /// </summary>
        /// <param name="graphElements"> The graph elements </param>
        /// <param name="resultTypeSpecification"> The result specification </param>
        /// <returns> </returns>
        private static IEnumerable<int> CreateResult(IEnumerable<AGraphElement> graphElements,
                                                     ResultTypeSpecification resultTypeSpecification)
        {
            switch (resultTypeSpecification)
            {
                case ResultTypeSpecification.Vertices:
                    return graphElements.OfType<VertexModel>().Select(_ => _.Id);

                case ResultTypeSpecification.Edges:
                    return graphElements.OfType<EdgeModel>().Select(_ => _.Id);

                case ResultTypeSpecification.Both:
                    return graphElements.Select(_ => _.Id);

                default:
                    throw new ArgumentOutOfRangeException("resultTypeSpecification");
            }
        }

        /// <summary>
        ///   Load the frontend
        /// </summary>
        private void LoadFrontend()
        {
            if (_ressources != null)
            {
                foreach (var memoryStream in _ressources)
                {
                    memoryStream.Value.Dispose();
                }
            }

            _ressources = FindRessources();
            _frontEndPre = Frontend.Pre;
            _frontEndPost = Frontend.Post;
        }

        /// <summary>
        ///   Find all ressources
        /// </summary>
        /// <returns> Ressources </returns>
        private static Dictionary<string, MemoryStream> FindRessources()
        {
            var ressourceDirectory = Environment.CurrentDirectory + System.IO.Path.DirectorySeparatorChar + "Service" +
                                     System.IO.Path.DirectorySeparatorChar + "REST" +
                                     System.IO.Path.DirectorySeparatorChar + "Ressource" +
                                     System.IO.Path.DirectorySeparatorChar;

            return Directory.EnumerateFiles(ressourceDirectory)
                .ToDictionary(
                    key => key.Split(System.IO.Path.DirectorySeparatorChar).Last(),
                    CreateMemoryStreamFromFile);
        }

        /// <summary>
        ///   Creates a memory stream from a file
        /// </summary>
        /// <param name="value"> The path of the file </param>
        /// <returns> MemoryStream </returns>
        private static MemoryStream CreateMemoryStreamFromFile(string value)
        {
            MemoryStream result;

            using (var file = File.OpenRead(value))
            {
                var reader = new BinaryReader(file);
                result = new MemoryStream(reader.ReadBytes((Int32) file.Length));
            }

            return result;
        }

        /// <summary>
        ///   Generates the properties.
        /// </summary>
        /// <returns> The properties. </returns>
        /// <param name='propertySpecification'> Property specification. </param>
        private static PropertyContainer[] GenerateProperties(
            Dictionary<UInt16, PropertySpecification> propertySpecification)
        {
            PropertyContainer[] properties = null;

            if (propertySpecification != null)
            {
                var propCounter = 0;
                properties = new PropertyContainer[propertySpecification.Count];

                foreach (var aPropertyDefinition in propertySpecification)
                {
                    properties[propCounter] = new PropertyContainer
                                                  {
                                                      PropertyId = aPropertyDefinition.Key,
                                                      Value =
                                                          Convert.ChangeType(aPropertyDefinition.Value.Property,
                                                                             Type.GetType(
                                                                                 aPropertyDefinition.Value.FullQualifiedTypeName,
                                                                                 true, true))
                                                  };
                    propCounter++;
                }
            }

            return properties;
        }

        /// <summary>
        ///   Gets the graph element properties.
        /// </summary>
        /// <returns> The graph element properties. </returns>
        /// <param name='vertexIdentifier'> Vertex identifier. </param>
        private PropertiesREST GetGraphElementProperties(string vertexIdentifier)
        {
            AGraphElement vertex;
            if (_fallen8.TryGetGraphElement(out vertex, Convert.ToInt32(vertexIdentifier)))
            {
                return new PropertiesREST
                           {
                               Id = vertex.Id,
                               CreationDate = DateHelper.GetDateTimeFromUnixTimeStamp(vertex.CreationDate),
                               ModificationDate =
                                   DateHelper.GetDateTimeFromUnixTimeStamp(vertex.CreationDate + vertex.ModificationDate),
                               Properties =
                                   vertex.GetAllProperties().ToDictionary(key => key.PropertyId,
                                                                          value => value.Value.ToString())
                           };
            }

            return null;
        }

        #endregion
    }
}