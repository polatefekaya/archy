import {createInterface} from "node:readline";
import Graph from "graphology";
import louvain from "graphology-communities-louvain";
import {createRequire} from "node:module";

const require=createRequire(import.meta.url), info=require("./package.json"), version=1;
for await (const line of createInterface({input:process.stdin,crlfDelay:Infinity})) process.stdout.write(`${JSON.stringify(await handle(line))}\n`);
async function handle(line) {
  let request; try { request=JSON.parse(line); } catch { return fail("unknown","invalid-request","Sidecar messages must be JSON.",false); }
  const id=get(request,"requestId")??"unknown";
  if(get(request,"protocolVersion")!==version||typeof id!=="string"||!id||!Array.isArray(get(request,"allowedRepositoryRelativePaths"))||typeof get(request,"payloadJson")!=="string") return fail(id,"invalid-request","The request envelope is invalid.",false);
  if(get(request,"method")==="handshake") return ok(id,{protocolVersion:version,sidecarName:"archy-louvain",toolVersion:`graphology:${info.dependencies.graphology};louvain:${info.dependencies["graphology-communities-louvain"]}`,capabilities:[{name:"weighted-community-detection",version:1}]});
  if(get(request,"method")!=="cluster_graph") return fail(id,"unknown-method","The sidecar method is not supported.",false);
  try { return ok(id,cluster(JSON.parse(get(request,"payloadJson")))); } catch(error) { return fail(id,"invalid-payload",error instanceof Error?error.message:"Invalid graph payload.",false); }
}
function cluster(payload) {
  if(!payload||!Array.isArray(payload.nodes)||!Array.isArray(payload.edges)||payload.nodes.length>100000||payload.edges.length>500000) throw new Error("A bounded node and edge graph is required.");
  const nodes=[...new Set(payload.nodes)].sort(); if(nodes.length!==payload.nodes.length||nodes.some(id=>typeof id!=="string"||!id)) throw new Error("Nodes must be unique non-empty identifiers.");
  const graph=new Graph({type:"undirected",multi:false,allowSelfLoops:false}); for(const node of nodes)graph.addNode(node);
  for(const edge of [...payload.edges].sort((a,b)=>`${a.source}\0${a.target}`.localeCompare(`${b.source}\0${b.target}`))) { if(!edge||!nodes.includes(edge.source)||!nodes.includes(edge.target)||edge.source===edge.target||!Number.isFinite(edge.weight)||edge.weight<=0) throw new Error("Edges require distinct known nodes and positive finite weights."); const [source,target]=edge.source<edge.target?[edge.source,edge.target]:[edge.target,edge.source]; if(graph.hasEdge(source,target)) graph.setEdgeAttribute(source,target,"weight",graph.getEdgeAttribute(source,target,"weight")+edge.weight); else graph.addEdge(source,target,{weight:edge.weight}); }
  const communities=louvain(graph,{randomWalk:false,rng:()=>0.5,getEdgeWeight:"weight"});
  const groups=new Map(); for(const node of nodes){const key=String(communities[node]);if(!groups.has(key))groups.set(key,[]);groups.get(key).push(node);} const ordered=[...groups.values()].sort((a,b)=>a[0].localeCompare(b[0]));
  return {toolVersion:info.dependencies["graphology-communities-louvain"],clusters:ordered.map((members,index)=>({clusterId:`community:${index+1}`,members}))};
}
function get(value,name){return value?.[name]??value?.[name[0].toUpperCase()+name.slice(1)];} function ok(id,result){return {ProtocolVersion:version,RequestId:id,IsSuccess:true,ResultJson:JSON.stringify(result),Error:null};} function fail(id,code,message,isRetryable){return {ProtocolVersion:version,RequestId:id,IsSuccess:false,ResultJson:null,Error:{Code:code,Message:message,IsRetryable:isRetryable}};}
