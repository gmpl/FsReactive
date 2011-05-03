﻿#light



namespace FsReactive

 open Misc
 
 module FsReactive = 


// Reactive

  type Time = float

  type 'a Behavior = Beh of (Time -> ('a * (unit -> 'a Behavior)))

  type 'a Event = Evt of (Time -> ('a option * (unit -> 'a Event)))

  let atB (Beh bf) = bf
  let atE (Evt bf) = bf


// memoization of Behaviors


  let memoB b =
    let cache = ref None
    let bref = ref b
    let compute b t = let (r, nb) = atB b t
                      cache := Some (t, r);
                      (r, nb)
    let rec bf   t = match !cache with
                     |Some (t0, r) when t=t0 -> (r, fun () -> resB)
                     |Some (t0, r) when t<t0 -> failwith "error"
                     |_ ->  match compute !bref t with
                            (r, nb) ->  (r, fun () -> bref := nb()
                                                      resB)
    and resB = Beh bf
    resB

    
  let memoE b =
    let cache = ref None
    let bref = ref b
    let compute b t = let (r, nb) = atE b t
                      cache := Some (t, r);
                      (r, nb)
    let rec bf   t = match !cache with
                     |Some (t0, r) when t=t0 -> (r, fun () -> resB)
                     |Some (t0, r) when t<t0 -> failwith "error"
                     |_ ->  match compute !bref t with
                            (r, nb) ->  (r, fun () -> bref := nb()
                                                      resB)
    and resB = Evt bf
    resB


// applicative functor 
    
  let rec pureB f = Beh (fun t -> (f, fun() -> pureB f))

  let rec (<.>) (Beh f) (Beh baf) = 
    let rec bf t = let (r,nb) = baf t
                   let (rf, nbf) = f t
                   (rf r, fun () -> nbf () <.>  nb ())
    Beh bf

  let rec pureE f = Evt (fun t -> (Some f, fun() -> pureE f))

  let rec (<..>) (Evt f) (Evt baf) = 
    let rec bf t = let (r,nb) = baf t
                   let (rfo, nbf) = f t
                   match rfo with
                   |Some rf -> (rf r, fun () -> (nbf()) <..> nb ())
                   |None -> (None, fun () -> (nbf()) <..> nb ()) 
    Evt bf


//Some common Behaviors

// timeB : Time Behavior

  let rec timeB = Beh (fun t -> (t, fun() -> timeB))
 
// deltaTimeB : Time -> Behavior 

  let deltaTimeB t0 = 
    let rec bf t0 t = (t-t0, fun() -> Beh (bf t))
    Beh (bf t0) 
                     


// switchB : 'a Behavior -> 'a Behavior Event -> 'a Behavior

  let rec switchB b e =
   let rec bf b e t = let (r,nb) = atB b t
                      let proc() = let (re, ne) = atE e t
                                   match re with
                                   |None -> Beh ( bf (nb()) (ne()))
                                   |Some newB -> Beh ( bf newB (ne()))
                      (r, proc)           
   Beh (bf b e)

// val untilB : 'a Behavior -> 'a Behavior Event -> 'a Behavior




  let rec untilB b e =
    let rec bf b e t =  let (r, nb) = atB b t
                        let proc() = let (re, ne) = atE e t
                                     match re with
                                     |None -> Beh (bf  (nb()) (ne()))
                                     |Some newB ->  ne ()
                                                    newB
                        (r, proc)
    Beh (bf b e)



// events

  let rec noneE = Evt (fun t -> (None, fun() -> noneE))
  let rec someE v = Evt (fun t -> (Some v, fun() -> someE v))

// val ( =>> ) : 'a Event -> ('a -> 'b) -> 'b Event
 
  let (=>>) evt f = 
    let proc evt = match evt with
                   |Some evt -> Some (f evt) 
                   |None -> None
    let rec bf evt t = let (r, nevt) = atE evt  t
                       (proc r, fun() -> (Evt (bf (nevt()) ) ))
    Evt (bf evt)
    
    
// val ( --> ) : 'a Event -> 'b -> 'b Event

  let rec (-->) ea b  =  ea =>> (fun _ -> b)
  
// val snapshotE : 'a Event -> 'b Behavior -> ('a * 'b) Event

  let rec snapshotE evt b =
    let rec bf evt b t = let (r,nb) = atB b t
                         let (re,nevt) = atE evt t
                         match re with
                         |Some v -> (Some(v, r), fun() -> Evt (bf (nevt()) (nb()) ))
                         |None -> (None, fun() -> Evt (bf (nevt()) (nb()) ))
    Evt (bf evt b)

// val snapshotBehaviorOnlyE : 'a Event -> 'b Behavior -> 'b Event

  let rec snapshotBehaviorOnlyE evt b = snapshotE evt b =>> (fun (_, x) -> x)
     
// val stepB : 'a -> 'a Event -> 'a Behavior

  let stepB (a:'a) (evt:'a Event) = switchB (pureB a) (evt =>> pureB)

 
// val stepAccumB : 'a -> ('a -> 'a) Event -> 'a Behavior

  let rec stepAccumB a evt = 
    let bf t = let (re, ne) = atE evt t
               match re with
               |Some f -> let na = f a
                          (na, fun() -> stepAccumB na (ne()) )
               |None -> (a, fun() -> stepAccumB a (ne()) )
    Beh bf
    

 
// val ( .|. ) : 'a Event -> 'a Event -> 'a Event
     
  let rec (.|.) ea eb = 
    let proc ra rb = match (ra, rb) with
                     |(Some _ , _) -> ra
                     |(None , Some _) -> rb
                     |(None , None) -> None
    let bf ea eb t = let (ra, nea) = atE ea t
                     let (rb, neb) = atE eb t
                     (proc ra rb, fun() -> nea()  .|. neb() )
    Evt (bf ea eb)


// orE : ('a -> 'a -> 'a) -> 'a Event -> 'a Event -> 'a Event

  let  orE comp ea eb = 
    let proc ra rb = match (ra, rb) with
                     |(Some a , Some b) -> Some (comp a b)
                     |(Some _ , None) -> ra
                     |(None , Some _) -> rb
                     |(None , None) -> None
    let rec bf ea eb t = let (ra, nea) = atE ea t
                         let (rb, neb) = atE eb t
                         (proc ra rb, fun() -> Evt (bf (nea()) (neb())) )
    Evt (bf ea eb)



// val ( .&. ) : 'a Event -> 'b Event -> ('a * 'b) Event
 
  let rec (.&.) ea eb = 
    let proc ra rb = match (ra, rb) with
                     |(Some va , Some vb) -> Some (va,vb)
                     |_ -> None
    let bf ea eb t = let (ra, nea) = atE ea t
                     let (rb, neb) = atE eb t
                     (proc ra rb, fun() -> nea() .&. neb() )
    Evt (bf ea eb)
    

// val iterE : 'a Event -> 'b list -> ('a * 'b) Event

  let rec iterE evt l =  
    match l with 
    |[] -> noneE
    |head::tail  -> let bf t = let (re, ne) = atE evt t
                               match re with
                               |Some v -> (Some (v,head), fun() -> iterE (ne()) tail)
                               |None -> (None, fun() -> iterE (ne()) l)
                    Evt bf
                    
                    
// val loopE : 'a Event -> 'b list -> ('a * 'b) Event
                    
  let rec loopE evt l =  
    match l with 
    |[] -> noneE
    |head::tail  -> let bf t = let (re, ne) = atE evt t
                               match re with
                               |Some v -> (Some (v,head), fun() -> loopE (ne()) (tail @ [head]))
                               |None -> (None, fun() -> loopE (ne()) l)
                    Evt bf

// val whileE : bool Behavior -> unit Event

  let rec whileE b =
    let bf t = let (r,nb) = atB b t
               if r 
               then (Some (), fun() -> whileE (nb()))
               else (None, fun() -> whileE (nb()))
    Evt bf


// val whenE : bool Behavior -> unit Event

  let whenE b =
    let rec bf b previous t = 
        let (r,nb) = atB b t
        match (previous, r) with
        |(false, true) -> (Some (), fun() -> Evt (bf (nb()) r))
        |(false, false) -> (None, fun() -> Evt (bf (nb()) r))
        |(true, true) -> (None, fun() -> Evt (bf (nb()) r))
        |(true, false) -> (None, fun() -> Evt (bf (nb()) r))
    Evt (bf b false)


// val whenBehaviorE : 'a option Behavior -> 'a Event

  let whenBehaviorE b =
    let rec bf b previous t = 
        let (r,nb) = atB b t
        match (previous, r) with
        |(None, Some v) -> (Some v, fun() -> Evt (bf (nb()) r))
        |(None, None) -> (None, fun() -> Evt (bf (nb()) r))
        |(Some v1, Some v2) -> (None, fun() -> Evt (bf (nb()) r))
        |(Some v, None) -> (None, fun() -> Evt (bf (nb()) r))
    Evt (bf b None)
                











